namespace LinkedApp.Client;

// A genuine cross-project reference: CoreOnly is declared by LinkedApp.Core and by nothing else, so this
// edge is one the project files really do declare.
public class Consumer
{
    public LinkedApp.Core.CoreOnly Subject;
}
