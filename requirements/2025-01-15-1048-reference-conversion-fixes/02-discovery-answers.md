# Discovery Answers

## Q1: Should the system verify that assembly public key tokens match when converting references to packages?
**Answer:** Yes

## Q2: Do you need to preserve references as-is when they cannot be found as NuGet packages and are not framework references?
**Answer:** Yes

## Q3: Should the system maintain a more comprehensive list of SDK-implicit framework references for .NET Core/.NET 5+?
**Answer:** Focus on .NET Framework for now. This is primarily for converting legacy apps to the newer SDK format.

## Q4: Do you want the migration to preserve the full reference metadata (Version, Culture, PublicKeyToken) for unconverted references?
**Answer:** Yes. Also if we do successfully match to a NuGet package, we should keep the same version.

## Q5: Should the system log warnings when it cannot definitively match a reference to the correct package?
**Answer:** Yes