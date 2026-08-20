namespace Ghseeli.BusinessApi.InternalServices;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class InternalServiceOperationAttribute : Attribute
{
    public InternalServiceOperationAttribute(string operation)
    {
        Operation = operation;
    }

    public string Operation { get; }
}
