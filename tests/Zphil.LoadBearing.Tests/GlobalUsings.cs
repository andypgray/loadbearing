// The project's global using directives — deliberately one, and the bar for a second is high.
//
// Ct is the running test's cancellation token, and nearly every test here passes one. Before this file
// each class that wanted it redeclared the same private property, so the name was already universal and
// the import only stopped it being retyped. That is the whole case: a global using is invisible at the
// file that depends on it, and a reader who cannot find where a name comes from is paying for the
// convenience. Worth it for one name every file already spelled and none can mistake; rarely otherwise.

global using static Zphil.LoadBearing.Tests.TestSupport.TestCancellation;
