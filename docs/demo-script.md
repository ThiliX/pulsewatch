# Demo script

A five-minute walkthrough that shows the whole product working end to end. Run the app locally first (see [Getting started](../README.md#getting-started)).

1. **Register** a new account.
2. **Add a monitor** that points at a test endpoint you control, so you can make it fail on demand.
3. **Show checks arriving** on the monitor page: the uptime bar, response times and the latest results.
4. **Make the endpoint fail.** Watch the monitor go to Degraded, then to Down once the failure threshold is reached.
5. **Show the alert email** in Mailpit (<http://localhost:8025>).
6. **Make the endpoint healthy again.** The incident resolves and a recovery email is sent.
7. **Show the incident timeline** and the email delivery history.
8. **Show the tests in CI:** the SSRF protection tests and the cross-user isolation tests.
