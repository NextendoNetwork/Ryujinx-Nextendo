# HOME presence publisher regression

Run with Python 3 and a .NET SDK (no package restore or network access needed):

```
python tests/nextendo-home-presence/verify.py --dotnet /path/to/dotnet
```

The harness extracts the production DoPublish method and compiles it against
controlled account/title state and a fake HTTP transport. It checks HOME's
real ID, online status and Ryujinx description; an ordinary game's unchanged
mode and opaque join field; and the previous behavior of other system programs.
No credentials are loaded and no requests leave the process. This does not
replace compiling Ryujinx or verifying the native client/server UI end to end.
