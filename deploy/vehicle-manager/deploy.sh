#!/usr/bin/env bash
# Deploy Vehicle Manager: deploy/vehicle-manager/deploy.sh [user@host]. See deploy/deploy.sh.
exec "$(dirname "$0")/../deploy.sh" vehicle-manager "$@"
