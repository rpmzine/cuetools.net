# Generating repository changelogs

This script uses the developer's clone of the repository to generate a changelog via `git log`.

## Requirements:
- Node.js > 10.0.0
- Current clone of https://github.com/microsoft/botframework-sdk
- Current clone of the repository and branch to generate a changelog from

## Preferred setup:
- **Each repository should only permit squash merges**
  - In most scenarios, the developer calling the script will need to manually update the final contents with better descriptions.
  - If the repository accepts all PR merge types, the developer will need to perform more manual edits due to the commit messages. 
  - Squash merges on GitHub result in commit messages like the following: `"Fix fetch in token server used in E2E Streaming Test (#2944)"`
  - Other merges will result in commit messages such as: `"Merge pull request #1411 from <fork/branch>"`
  - This results in a non-descriptive format:
  ```md
  - Merge pull request #4661 from microsoft/johtaylo/private-cloud-patch [[PR 4661]](https://github.com/microsoft/botbuilder-dotnet/pull/4661)
  ```
- **Developers should generate the changelog from the branch that is about to be released**
  - Instead of generating it on the default or development branch (e.g `main`), the script should be run on the version branch (e.g. `4.11`).

## Steps
_The steps below use [botbuilder-dotnet](https://github.com/microsoft/botbuilder-dotnet) as an example._
- Clone the target repository and switch to the selected branch for generating a changelog.
  - `git clone -b 4.11 https://github.com/microsoft/botbuilder-dotnet && cd botbuilder-dotnet`
- Run `git log` with the following flags and arguments.
  - `git log --show-pulls --pretty=tformat:"%s" c6ff4a4.. > dotnet.txt`
  - `c6ff4a4..` represents the desired range of commits to log **after commit `c6ff4a4`**.
  - In this example, `c6ff4a4` was the latest commit included in the previous changelog. 
- Move the `dotnet.txt` file to the folder containing [git-log.js](./git-log.js).
- Navigate to `botframework-sdk\tools\changelog` in your CLI.
- Execute the following command to generate the changelog.
  - `node git-log.js dotnet.txt`
  - With this command, the changelog can be found as `dotnet.md`.
  - `git-log.js` takes an optional second argument which is used as the filename for the output. (Note: the files content will still be [Markdown](https://daringfireball.net/projects/markdown))

The contents of the file should look like the following snippet generated for the 4.11.0 release of the Bot Framework SDK for .NET:

```md
- Teams: MeetingParticipantInfo.InMeeting might be null, if unknown [[PR 4868]](https://github.com/microsoft/botbuilder-dotnet/pull/4868)
- Fix issues with ExpectReplies and Invoke [[PR 4845]](https://github.com/microsoft/botbuilder-dotnet/pull/4845)
- Enable middleware in TestScript and refactor BeginSkill tests [[PR 4866]](https://github.com/microsoft/botbuilder-dotnet/pull/4866)
```

## Questions and Help
For bug reports, feature requests or other feedback, please file an issue in https://github.com/microsoft/botframework-sdk.

## Additional reading
- [About merge methods on GitHub](https://docs.github.com/en/free-pro-team@latest/github/administering-a-repository/about-merge-methods-on-github)
  - [Squashing your merge commits](https://docs.github.com/en/free-pro-team@latest/github/administering-a-repository/about-merge-methods-on-github#squashing-your-merge-commits)
- [Configuring commit squashing for pull requests](https://docs.github.com/en/free-pro-team@latest/github/administering-a-repository/configuring-commit-squashing-for-pull-requests)
- [Git - git-log Documentation](https://git-scm.com/docs/git-log)
  - [Commit Limiting](https://git-scm.com/docs/git-log#_commit_limiting) (e.g. `--since`, `--before` options)
- [Git - gitrevisions Documentation](https://git-scm.com/docs/gitrevisions)