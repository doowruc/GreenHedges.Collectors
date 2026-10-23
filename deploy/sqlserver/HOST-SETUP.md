# SQL Server: host setup

SQL Server 2025 Express in Docker, for the home server's web apps (so far Vehicle Manager), on `127.0.0.1:1433` only.
Needs Docker (Libre collector's [HOST-SETUP §9](../libre/HOST-SETUP.md)). How it's set up and why: the comments in
[`compose.yaml`](compose.yaml). Each app's own login and database are created in its own host setup.

## 1. Folders and the backups group (on the server)

```bash
sudo groupadd --system sqlbackups
```

```bash
sudo install -d -m 0755 /opt/sqlserver /var/lib/sqlserver && sudo install -d -m 0750 /etc/sqlserver
```

```bash
sudo install -d -m 0700 -o 10001 -g 10001 /var/lib/sqlserver/data && sudo install -d -m 2770 -o 10001 -g sqlbackups /var/lib/sqlserver/backups
```

## 2. The sa password (on the server)

```bash
sudo install -m 0600 /dev/null /etc/sqlserver/sqlserver.env && echo "MSSQL_SA_PASSWORD=$(openssl rand -base64 24 | tr -d '/+=')Aa1" | sudo tee /etc/sqlserver/sqlserver.env > /dev/null
```

Save it in your password manager (e.g. "Home server SQL Server sa"):

```bash
sudo cat /etc/sqlserver/sqlserver.env
```

## 3. Install and start (on the Mac)

```bash
deploy/sqlserver/deploy-sqlserver.sh
```

It ends by printing SQL Server's version and `Express Edition (64-bit)`.

## 4. Check (on the server)

```bash
sudo docker compose -f /opt/sqlserver/compose.yaml ps
```

```bash
sudo ss -ltnp | grep 1433
```

Expect `healthy`, and `127.0.0.1:1433` only.

## Updating

Change the image tag in `deploy/sqlserver/compose.yaml` (and the repo root's `compose.yaml`, to match) in a PR, then
re-run `deploy/sqlserver/deploy-sqlserver.sh`.
