namespace App.Legacy;

/// <summary>
///     Name-carrier for the frozen-scope facade in the <c>Sources.Containment</c> fixture:
///     <c>typeof(IFacade)</c> has full name <c>App.Legacy.IFacade</c>, matching the extracted codebase
///     type, so the checker resolves a <c>BoundaryOnlyVia(typeof(IFacade))</c> operand by full name (the
///     established Stubs trick). Compile-time only — referenced via <c>typeof</c>, never a solution member.
/// </summary>
public interface IFacade;