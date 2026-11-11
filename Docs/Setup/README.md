---
navigation.order: 10
navigation.placement: top
description: Install MDU as a .NET tool, Docker image, or portable executable.
---

# Setup

Install MDU globally as a .NET tool:

```sh
dotnet tool install --global mdu
```

This installation requires the [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0). For [repository-local installation](DotNet-Tool-Installation.md), the [Docker image](Docker-Installation.md), or a [portable executable](Portable-Installation.md), see the detailed guides.

## First steps

Create a starter project, preview it, and build the site:

```sh
mdu new
mdu serve
mdu render
```

![Creating, previewing, and building a documentation site with mdu new, mdu serve, and mdu render](../assets/demo.gif)

See [`mdu new`](../Usage/new.md), [`mdu serve`](../Usage/serve.md), and [`mdu render`](../Usage/render.md) for the options.
