# Discovery Questions

## Q1: Should the system verify that assembly public key tokens match when converting references to packages?
**Default if unknown:** Yes (prevents incorrect package substitution based on name alone)

## Q2: Do you need to preserve references as-is when they cannot be found as NuGet packages and are not framework references?
**Default if unknown:** Yes (likely custom or private DLLs that should remain unchanged)

## Q3: Should the system maintain a more comprehensive list of SDK-implicit framework references for .NET Core/.NET 5+?
**Default if unknown:** Yes (prevents unnecessary package references for built-in types)

## Q4: Do you want the migration to preserve the full reference metadata (Version, Culture, PublicKeyToken) for unconverted references?
**Default if unknown:** Yes (maintains exact reference specifications for custom DLLs)

## Q5: Should the system log warnings when it cannot definitively match a reference to the correct package?
**Default if unknown:** Yes (helps users identify potential issues requiring manual review)