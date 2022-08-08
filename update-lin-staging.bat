pwsh scripts\SteamDownloader.ps1 -steam_appid 258550 -steam_branch staging -platform linux -deps_dir "../raw-deps"
pwsh scripts\unprivate-dependencies.ps1 -outputPath "deps/linux/" -inputPath "raw-deps/linux/RustDedicated_Data/Managed"
PAUSE