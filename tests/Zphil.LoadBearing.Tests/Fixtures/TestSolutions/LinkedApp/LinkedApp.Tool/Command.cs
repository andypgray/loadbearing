namespace LinkedApp.Tool;

// This project's only outward type reference, and it points at the linked-in copy of Widget that this
// project compiles itself — never across a project boundary.
public class Command
{
    public LinkedApp.Shared.Widget Subject;
}
