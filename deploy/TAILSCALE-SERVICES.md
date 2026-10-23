# Tailscale Services: one-off setup

Each web app on the server gets its own tailnet address, `https://<name>.<tailnet>.ts.net`, with no port or path:
`tailscale serve --service=svc:<name>` publishes it, with HTTPS. Only devices on the tailnet can reach it. Services is in
public beta (free on all plans for now); if that changes, a port on the server's own name works instead
(`tailscale serve --bg --https=<port> …`), and only the app's address and Auth0 callback change.

A host must have a tag (not a user) to serve a Service. These steps make the server `tag:server` and let your devices
reach its Services. Do them once; each app's host setup then adds its own Service (e.g. Vehicle Manager:
[vehicle-manager/HOST-SETUP.md](vehicle-manager/HOST-SETUP.md)).

## 1. Access controls (admin console)

**Access controls** → edit the policy file, and add (merging into any sections already there):

```jsonc
"tagOwners": {
  "tag:server": ["autogroup:admin"],
},
"grants": [
  // Your devices may reach the server's web apps.
  { "src": ["autogroup:member"], "dst": ["svc:vehicles"], "ip": ["443"] },
],
"autoApprovers": {
  "services": {
    "svc:vehicles": ["tag:server"],
  },
},
```

Each new app adds its `svc:<name>` to both lists.

## 2. Tag the server (admin console)

**Machines** → the server → **⋯** → **Edit tags** → add `tag:server` → **Save**. No re-authentication is needed. The
machine stops belonging to your user account (a device has tags or a user, not both); its key expiry stays off, and
SSH, `tailscale serve` for Nightscout and everything else carry on.

## 3. Define the Service (admin console)

**Services** → **Advertise** → **Define a Service**: name `vehicles`, a description (e.g. "Vehicle Manager"), endpoint
`tcp:443`. Then **Add service**.

## 4. Check (on the server)

```bash
tailscale status --self --json | grep -A2 '"Tags"'
```

It lists `tag:server`.
