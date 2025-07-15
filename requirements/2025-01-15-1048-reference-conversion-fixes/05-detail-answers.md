# Detail Answers

## Q6: Should the system create a new return type from AssemblyReferenceConverter that includes both converted PackageReferences AND unconverted References?
**Answer:** Yes

## Q7: When matching assembly versions to package versions, should the system prefer exact version matches over latest stable versions?
**Answer:** Yes

## Q8: Should unconverted references with HintPath be preserved with their original HintPath in the new project format?
**Answer:** Yes

## Q9: Should the system add XML comments in the generated project file explaining why certain references couldn't be converted?
**Answer:** No

## Q10: Should PublicKeyToken validation failures result in skipping the conversion entirely or just logging a warning?
**Answer:** If we can't match the public key to a NuGet package, it's probably safer to leave it as a local reference