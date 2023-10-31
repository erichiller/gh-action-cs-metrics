using System;
using System.Threading;
using System.Threading.Tasks;

namespace SomeRoot.SampleProject;

public interface IGenericOne<T1> {
    public T1 GenericTOne { get; }
}

public interface IGenericTwo<T1,T2> : IGenericOne<T1> {
    public T2 GenericTTwo { get; }
}

public interface IIntFoo {
    public int Foo { get; set; }
}

public class Class1 : IIntFoo {
    public int Foo { get; set; }
    
    public Class2 Class2Prop { get; }
}

public class Class2 : IGenericTwo<string, int> {
    public required string StringProp { get; init; }
    public string[] StringArrayProp { get; }
    public string GenericTOne { get; }
    public int GenericTTwo { get; }
}

public class ClassGeneric1<T> : IGenericOne<Class1> {
    public T MyT { get; }
    public Class1 GenericTOne { get; }
    
    public ValueTask DoThingsAsync() =>
         ValueTask.CompletedTask;
         
    public IDisposable DisposableReturningMethod() =>
        throw new NotImplementedException();
}

// URGENT: any sort of record struct breaks things!

// public readonly record struct ReadOnlyRecordStruct1(string StringPropertyA, int IntPropertyB);

// public record struct RecordStruct1(string StringPropertyA, int IntPropertyB);

public struct RegularStruct1 {
    public string StringPropertyA { get; set; }
}

public enum MyEnum1 {
    EnumMemberA,
    EnumMemberB
}

public record RecordClass1( string StringPropertyA, int IntPropertyB);