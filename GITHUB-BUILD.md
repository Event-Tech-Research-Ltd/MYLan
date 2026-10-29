# Build MYLan on GitHub

1. Extract this ZIP and open the MYLan-2026-v2.2 folder.
2. Copy its contents into the repository root, alongside MYLan.slnx. Include
   .github/workflows/build.yml and global.json. Do not upload the ZIP itself
   or nest the MYLan-2026-v2.2 folder inside the repository.
3. Commit and push to your branch (for example MYLanX).
4. Open Actions → Build MYLan installers. The push starts the build automatically.
5. Open that run. Once the jobs succeed, download the required Artifacts:
   - MYLan-Windows-x64-installer: Windows setup executable.
   - MYLan-Windows-x64-portable: self-contained Windows build.
   - MYLan-macOS-Intel-DMG: Intel Mac disk image.
   - MYLan-macOS-Apple-Silicon-DMG: Apple Silicon Mac disk image.
6. Extract the downloaded artifact ZIP to access the installer or DMG.

Manual runs: once this workflow also exists on the repository's default branch,
Actions shows Run workflow. Select MYLanX (or another branch containing the
workflow) before starting it. A push works without merging into the default branch.

Build tools are installed on GitHub's runners; no local SDK is needed. Artifacts
are retained for 30 days. This workflow does not publish a GitHub Release.

The macOS scripts use ad-hoc signing. These builds are not Developer ID signed
or notarised; Windows installers are also not certificate-signed. The workflow
checks packaging, not GUI operation or DHCP behaviour on your production network.

If a job fails, open its red step in Actions to read the error. Dependencies must
be downloadable and Actions must be enabled for the repository.
