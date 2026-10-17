#!/usr/bin/env bash
# Deploy the Weather collector: deploy/weather/deploy.sh [user@host]. See deploy/deploy.sh.
exec "$(dirname "$0")/../deploy.sh" weather "$@"
