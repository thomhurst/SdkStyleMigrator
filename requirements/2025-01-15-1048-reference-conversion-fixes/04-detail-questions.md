# Expert Requirements Questions

## Q6: Should the system create a new return type from AssemblyReferenceConverter that includes both converted PackageReferences AND unconverted References?
**Default if unknown:** Yes (allows CleanSdkStyleProjectGenerator to handle both types appropriately)

## Q7: When matching assembly versions to package versions, should the system prefer exact version matches over latest stable versions?
**Default if unknown:** Yes (maintains compatibility with existing code that depends on specific versions)

## Q8: Should unconverted references with HintPath be preserved with their original HintPath in the new project format?
**Default if unknown:** Yes (maintains references to local DLLs and third-party assemblies)

## Q9: Should the system add XML comments in the generated project file explaining why certain references couldn't be converted?
**Default if unknown:** No (keeps project files clean, rely on build logs for diagnostic information)

## Q10: Should PublicKeyToken validation failures result in skipping the conversion entirely or just logging a warning?
**Default if unknown:** No (log warning but attempt conversion - let user verify correctness)