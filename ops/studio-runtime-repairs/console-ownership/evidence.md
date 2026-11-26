# Verification scope

2026-11-26. Source-only publication; game acceptance is pending.

Observed against the two hashes in README: the supported original discards the successful allocation result and releases any present console in Close. The reviewed candidate changes only Run/Close and adds one ownership bool. Independent read-only review checked 219 existing methods, no short-branch overflow, unchanged other method attributes/PInvoke/locals/exception handlers, identical six assembly references and CLR v2.0.50727. The assembly has zero resources, so a resources-equal assertion is vacuous and is not claimed as a resource-copy exercise.

The isolated control uses real Windows console detachment. Original Close makes two independent CONOUT$ handles return error 6 and makes StreamWriter throw IOException. Borrowed candidate cases preserve both handles and writer, including a null REPL object and repeated Close. Owned cases still detach once. Abort calls are counted, and the null branch and allocation-result store are checked separately. Full Program.Run, actual UC.Abort and Unity Mono are outside this test.

The private observed game failure involved several plugins' normal exit logs reaching the same invalid BepInEx console listener writer after Unity.Console shutdown. That is consistent with the ownership fault and the isolated reproduction. No game log or installation information is reproduced in this public evidence. It is an inference about the game until normal startup and exit are tested with the exact candidate.

Public-source rerun completed with both projects building at zero warnings/errors, output hash equality, five native-case pass flags, five rejection cases and unchanged original hash. [verification-summary.json](verification-summary.json) contains those results without paths or raw logs. The public directory contains only source projects, scripts, documentation and that structured evidence; generated assemblies and extracted fixtures remain outside it. There are no automated claims of in-game compatibility.
