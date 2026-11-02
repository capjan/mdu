# Native .NET tool package evaluation

The user selected native release execution as the performance reference and requested evaluation of native tool packages with a portable fallback. This study changes no release workflow, global tool installation or published package.

On .NET SDK 10.0.403 / macOS arm64, a local prototype successfully produced:

- `mdu`: SDK tool-settings version 2 catalog, mapping `osx-arm64` to `mdu.osx-arm64` and `any` to `mdu.any`;
- `mdu.osx-arm64`: Native AOT executable, installed and executed as an arm64 Mach-O;
- `mdu.any`: the portable managed implementation. A fallback-only catalog/feed installed and ran this implementation separately.

The SDK selected the native implementation on this host. Supplying `--arch x64` did not force the portable branch of the catalog on this SDK/host; the fallback was therefore tested with an isolated catalog offering only `any`. This verifies the two implementations locally, not selection on every unsupported host.

## Reproduce without publishing

Use an isolated output and tool directory and a unique local prerelease version. The version below is a test parameter, not a second product version definition.

```sh
package_version=3.0.0-local.20261102.188.native.2
package_dir=/tmp/mdu-native-packages
dotnet pack Source/Mdu/Mdu.csproj -c Release -r osx-arm64 -p:PublishAot=true '-p:ToolPackageRuntimeIdentifiers="any;osx-arm64"' -p:Version="$package_version" --output "$package_dir"
dotnet pack Source/Mdu/Mdu.csproj -c Release -r any -p:PublishAot=false '-p:ToolPackageRuntimeIdentifiers="any;osx-arm64"' -p:Version="$package_version" --output "$package_dir"
dotnet pack Source/Mdu/Mdu.csproj -c Release -p:PublishAot=true '-p:ToolPackageRuntimeIdentifiers="any;osx-arm64"' -p:Version="$package_version" --output "$package_dir"
NUGET_PACKAGES=/tmp/mdu-native-install-cache dotnet tool install mdu --tool-path /tmp/mdu-native-tool --add-source "$package_dir" --version "$package_version"
/tmp/mdu-native-tool/mdu --help
```

The inner `any` package must explicitly disable `PublishAot`; requesting Native AOT for RID `any` fails in the compiler targets. For a shell invocation, preserve the inner quotation marks in the list property. Using `%3B` generated a catalog with a single literal `any;osx-arm64` entry on this SDK, which installation correctly rejected.

For the fallback check, copy the `mdu.any` package to another feed, create the catalog there with `-p:ToolPackageRuntimeIdentifiers=any`, and install with another isolated `NUGET_PACKAGES` and `--tool-path`. Inspect the installed `.store` directory and run its command. The normal catalog and fallback-only catalog share a test version but must remain in separate feeds/caches.

## Before adopting this in releases

The current four native release targets are Linux x64, Windows x64, macOS x64 and macOS arm64. Native child packages would need to be built on the corresponding existing runners, then combined with the portable child and catalog. Preserve the existing CLI/package identity, portable coverage, single git-cliff version, and release-resume behavior.

Verify the executable **inside** each macOS NuGet package retains the intended Developer ID signature and notarization preparation; rerunning SDK publication during packing can replace a previously signed publish copy. Validate ownership and Trusted Publishing permissions for every new child package ID, then test install/update and fallback resolution on the supported runners. The local prototype does not establish those release-system properties, and nothing was uploaded to NuGet or GitHub.
