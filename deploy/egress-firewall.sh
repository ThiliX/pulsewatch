#!/bin/sh
# SSRF layer 4: stop the PulseWatch worker from reaching private, local and metadata addresses, whatever the
# application does. Applies to traffic leaving the worker's "egress" Docker network (bridge pw-egress, set in
# docker-compose.prod.yml). Traffic to PostgreSQL uses the separate "backend" network and is not affected.
#
#   sudo ./deploy/egress-firewall.sh apply    # add the rules (safe to run again)
#   sudo ./deploy/egress-firewall.sh remove   # take them away
#   sudo ./deploy/egress-firewall.sh status   # show them
#
# Rules are not saved across reboots; docs/deployment.md §6 shows a systemd unit that applies them at boot.
# Needs iptables (Docker's default firewall backend). IPv6 is not enabled on the egress network, so there is
# no IPv6 path to filter.
#
# Allow one private address (for example an SMTP relay on your LAN) with:
#   sudo PW_EGRESS_ALLOW="10.0.0.25/32" ./deploy/egress-firewall.sh apply

set -eu

BRIDGE="${PW_EGRESS_BRIDGE:-pw-egress}"
CHAIN="PULSEWATCH-EGRESS"
ALLOW="${PW_EGRESS_ALLOW:-}"

# Same IPv4 ranges as PublicAddressPolicy.cs (not globally reachable, or able to reach the host).
BLOCKED="0.0.0.0/8 10.0.0.0/8 100.64.0.0/10 127.0.0.0/8 169.254.0.0/16 172.16.0.0/12 192.0.0.0/24 \
192.0.2.0/24 192.88.99.0/24 192.168.0.0/16 198.18.0.0/15 198.51.100.0/24 203.0.113.0/24 224.0.0.0/4 240.0.0.0/4"

remove() {
  while iptables -D DOCKER-USER -i "$BRIDGE" -j "$CHAIN" 2>/dev/null; do :; done
  while iptables -D INPUT -i "$BRIDGE" -j REJECT 2>/dev/null; do :; done
  iptables -F "$CHAIN" 2>/dev/null || true
  iptables -X "$CHAIN" 2>/dev/null || true
}

apply() {
  if ! iptables -n -L DOCKER-USER >/dev/null 2>&1; then
    echo "The DOCKER-USER chain does not exist. Is Docker running with its iptables firewall?" >&2
    exit 1
  fi

  remove
  iptables -N "$CHAIN"
  for network in $ALLOW; do
    iptables -A "$CHAIN" -d "$network" -j RETURN
  done
  for network in $BLOCKED; do
    # REJECT rather than DROP, so a blocked check fails at once instead of waiting for its timeout.
    iptables -A "$CHAIN" -d "$network" -j REJECT --reject-with icmp-net-prohibited
  done
  iptables -A "$CHAIN" -j RETURN

  # Forwarded traffic from the worker to other machines and networks.
  iptables -I DOCKER-USER -i "$BRIDGE" -j "$CHAIN"
  # Traffic from the worker to the host itself (SSH, other services listening on the host).
  iptables -I INPUT -i "$BRIDGE" -j REJECT

  echo "PulseWatch egress rules applied to bridge $BRIDGE."
}

status() {
  iptables -n -v -L DOCKER-USER | grep -E "Chain|$CHAIN" || true
  iptables -n -v -L INPUT | grep -E "Chain|$BRIDGE" || true
  iptables -n -v -L "$CHAIN" 2>/dev/null || echo "Chain $CHAIN does not exist (rules not applied)."
}

case "${1:-}" in
  apply) apply ;;
  remove) remove; echo "PulseWatch egress rules removed." ;;
  status) status ;;
  *) echo "Usage: $0 apply|remove|status" >&2; exit 2 ;;
esac
