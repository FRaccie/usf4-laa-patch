# laa-patch

Sets the Large Address Aware flag (0x20) in a 32-bit PE exe, and recomputes the PE checksum.
Meant for SSFIV.exe, which otherwise runs out of its 2 GB address space.

    powershell -ExecutionPolicy Bypass -File laa-patch.ps1 -Path SSFIV.exe            # writes SSFIV.laa.exe beside it
    ... -Path SSFIV.exe -InPlace    # makes SSFIV.exe.bak, patches the file itself
    ... -Path SSFIV.exe -Check      # prints the current flag
    ... -Path SSFIV.exe -Restore    # puts the .bak back

It does NOT make the game 64-bit: a 32-bit LAA process gets 4 GB of address space, and only on 64-bit Windows.
Steam "verify files" undoes in-place patching. Always keep the .bak. Run `test-laa-patch.ps1` to self-check.

## Windows app

`app/LaaPatch.cs` is the same patch with a window: pick SSFIV.exe, patch it (keeps SSFIV.exe.bak), restore it,
or patch a copy. Build with `app\build.cmd` (uses the .NET Framework 4 csc.exe that ships with Windows).
`app\click-test.ps1` clicks through the app on a temporary copy.
