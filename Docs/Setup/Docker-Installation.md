---
navigation.order: 12
---

# Docker

The release workflow publishes [`capjan/mdu` on Docker Hub](https://hub.docker.com/r/capjan/mdu) for Linux `amd64` and `arm64`. It tags each release with its version and updates `latest`.

Run MDU in the current folder by mounting it at `/work`:

```sh
docker run --rm \
  --volume "$PWD:/work" \
  --workdir /work \
  --user "$(id -u):$(id -g)" \
  capjan/mdu lint
```

Render the site with:

```sh
docker run --rm \
  --volume "$PWD:/work" \
  --workdir /work \
  --user "$(id -u):$(id -g)" \
  capjan/mdu render
```

The compact container includes the Native AOT `mdu` executable, its Alpine runtime dependencies, Git, CA certificates, and its license notices. [`mdu.import`](../Features/Repository-Imports.md) and Git-derived last-changed dates are available when the mounted documentation folder has the required repository data. Public imports work without credentials; private imports need Git credentials configured inside the container. `mdu serve` listens on the container's loopback interface; use `mdu render` to generate a site from Docker.

## CI/CD container

For CI/CD runners such as GitLab CI or GitHub Actions where jobs run shell commands directly, use [`capjan/mdu-ci`](https://hub.docker.com/r/capjan/mdu-ci). Unlike `capjan/mdu` which defaults to the `mdu` entrypoint, `capjan/mdu-ci` leaves the entrypoint open and provides a default shell so you can run `mdu` commands in your job scripts without overriding the container entrypoint. See [GitLab Pages](../Publish/GitLab.md) for an example.
