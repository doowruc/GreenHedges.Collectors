#!/usr/bin/env bash
# Deploy the Libre collector: deploy/libre/deploy.sh [user@host]. See deploy/deploy.sh.
exec "$(dirname "$0")/../deploy.sh" libre "$@"
