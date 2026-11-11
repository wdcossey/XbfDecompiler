namespace LibXbf.Output;

public interface IXbfOutput<out T>
{
    T GetOutput(XbfFile file);
}
