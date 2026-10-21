# RustDesk OSS integration pilot

This is a transport-only pilot, not the OmniDesk remote-control integration.
RustDesk Server OSS 1.1.16 runs in a separate container. Native OSS clients use
RustDesk's standard transport: TCP 21115-21117 and UDP 21116. The Compose
overlay publishes those ports; Docker host publication alone does not guarantee
reachability through the cloud security group, host firewall, or upstream NAT.
Do not expose TCP 21118/21119 for desktop clients in this pilot. The existing
Caddy WSS routes were removed because the tested OSS desktop client attempts
plain `ws://` and receives Caddy's HTTPS redirect; WSS client mode requires
RustDesk Server Pro. The pilot forces relay mode, so remote screen traffic
traverses the server rather than creating direct peer-to-peer connections.

Before connecting an external test device, an operator must verify inbound
reachability for TCP 21115, 21116, 21117 and UDP 21116. No host firewall,
cloud firewall, router, or system service is changed by this repository.
Use the exact public ID key shown in the server settings/logs and leave the API
field blank for OSS.

The service is added with `compose.rustdesk-pilot.yaml`, and its database and
server keys live in the Docker volume `rustdesk_pilot_data`. Preserve this
volume for the test; deleting it rotates the server identity and invalidates
the configured client key.

When recreating or updating the production API while this pilot is enabled,
include all three Compose files (`compose.yaml`, `compose.direct.yaml`, and
`compose.rustdesk-pilot.yaml`). Omitting the RustDesk overlay silently removes
the API's ID server, relay, and public-key settings even though the RustDesk
container itself remains healthy. For example:

```bash
docker compose -p omnidesk-production \
  -f compose.yaml -f compose.direct.yaml -f compose.rustdesk-pilot.yaml \
  up -d --no-deps api
```

OmniDesk now stores an administrator-verified RustDesk peer ID for each device.
The Desktop remoto page displays the ID server, relay server, blank OSS API
server field, and public server key for client setup. An operator with the
`remote.control` permission can request launch from the device page; OmniDesk
checks the permission and device revocation state and records the launch request
in the audit log. The browser opens the locally installed RustDesk client using
the peer ID and configured server key.

This launch handoff is not an embedded browser session. RustDesk handles the
actual connection, consent/password prompt, session status, and disconnect;
OmniDesk cannot attest those outcomes or remotely revoke a live RustDesk
connection. Install and configure RustDesk on both endpoints, keep target
access attended for this pilot, and confirm the peer ID belongs to the selected
OmniDesk device before saving the mapping. No remote password is stored or put
in the launch URI. The administrator download area offers a separately
packaged, pinned Windows x64 installer and copies for the ID server, relay,
blank API server, and public key. The API serves it only to administrators,
checks the pinned SHA-256 before every download, and audits the request. The
installer does not preconfigure RustDesk, set a password, enable unattended
access, or bundle RustDesk into the OmniDesk Agent. Configure the downloaded
client manually and keep access attended during this pilot.

RustDesk supports file transfer and a remote terminal when enabled in the
client's permissions; these controls are governed by RustDesk and are not
currently enforced by OmniDesk. The MeshCentral session management panel was
removed from the Desktop tab. OmniDesk's separate Agent Terminal/PowerShell and
file transfer tabs still use the legacy MeshCentral session service.
