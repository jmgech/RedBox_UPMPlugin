// REDboxApiCompatibilityRequirement.cs
// This file belongs to the REDbox.Compat assembly (no defineConstraints), so it
// compiles on ALL Unity API Compatibility Level settings. It emits a single clear
// compile-time error when the project is misconfigured, replacing the many cryptic
// CS0234/CS0246 errors that would otherwise flood the console.

#if !NET_4_6 && !NET_UNITY_4_8
#error [REDbox] This package requires the .NET Framework 4.x API Compatibility Level. \
Fix: Edit > Project Settings > Player > Other Settings > Api Compatibility Level > \
select ".NET Framework". Then re-open the project or reimport the package.
#endif
