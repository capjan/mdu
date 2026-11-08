---
navigation.order: 2
---

# GitLab

GitLab CI/CD can render your documentation and publish it with GitLab Pages. Add this `.gitlab-ci.yml` to the root of your repository; it publishes when files under `docs/` change on the default branch:

```yaml title=".gitlab-ci.yml"
pages:
  stage: deploy
  image: capjan/mdu-ci:latest
  script:
    - mdu lint
    - mdu render
  pages:
    publish: site
  rules:
    - if: '$CI_PIPELINE_SOURCE == "push" && $CI_COMMIT_BRANCH == $CI_DEFAULT_BRANCH'
      changes:
        - docs/**/*
```

The job uses the CI-ready MDU image `capjan/mdu-ci:latest` from Docker Hub. Unlike the default CLI image `capjan/mdu`, the CI image leaves the container entrypoint open so GitLab Runner can directly start its job shell and run scripts.

`mdu lint` and `mdu render` default to the `./docs` source folder, and `mdu render` outputs to `./site` by default, matching `pages.publish: site`. `mdu lint` fails the job when it reports errors, so a page with a broken link is not published; remove that line if you want to publish without the check. GitLab publishes the generated `site` folder; since GitLab 17.10 it is included as a job artifact automatically. Make sure a GitLab Runner is enabled for the project.

> [!TIP]
> If you prefer using the standard CLI image `capjan/mdu:latest`, override its entrypoint in your job:
>
> ```yaml
> image:
>   name: capjan/mdu:latest
>   entrypoint: [""]
> ```

> [!NOTE]
> If your source folder has another name, pass it to both commands, for example `mdu lint content` and `mdu render content site`, and update `docs/**/*` to match.

[GitLab Pages documentation](https://docs.gitlab.com/user/project/pages/)
