namespace MyApp.Web;

public interface IHandler<T>
{
    void Handle(T message);
}
