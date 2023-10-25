using System;
using System.Threading;
using System.Threading.Tasks;

namespace SomeRoot.SampleProject;


public interface IIntFoo {
    public int Foo { get; set; }
}

public class Class1 : IIntFoo {
    public int Foo { get; set; }
    
    public Class2 Class2Prop { get; }
}

public class Class2 {
    public required string StringProp { get; init; }
    public string[] StringArrayProp { get; }
}

public class ClassGeneric1<T> {
    public T MyT { get; }
    
    public ValueTask DoThingsAsync() =>
         ValueTask.CompletedTask;
         
    public IDisposable DisposableReturningMethod() =>
        throw new NotImplementedException();
    
}