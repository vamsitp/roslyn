﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using Microsoft.CodeAnalysis.CSharp.Test.Utilities;
using Microsoft.CodeAnalysis.Emit;
using Xunit;

namespace Microsoft.CodeAnalysis.CSharp.DynamicAnalysis.UnitTests
{
    public class DynamicInstrumentationTests : CSharpTestBase
    {
        const string InstrumentationHelperSource = @"
namespace Microsoft.CodeAnalysis.Runtime
{
    public static class Instrumentation
    {
        private static bool[][] _payloads;
        private static System.Guid _mvid;

        public static bool[] CreatePayload(System.Guid mvid, int methodIndex, ref bool[] payload, int payloadLength)
        {
            if (_mvid != mvid)
            {
                _payloads = new bool[100][];
                _mvid = mvid;
            }

            if (System.Threading.Interlocked.CompareExchange(ref payload, new bool[payloadLength], null) == null)
            {
                _payloads[methodIndex] = payload;
                return payload;
            }

            return _payloads[methodIndex];
        }

        public static void FlushPayload()
        {
            Console.WriteLine(""Flushing"");
            if (_payloads == null)
            {
                return;
            }
            for (int i = 0; i < _payloads.Length; i++)
            {
                bool[] payload = _payloads[i];
                if (payload != null)
                {
                    Console.WriteLine(i);
                    for (int j = 0; j < payload.Length; j++)
                    {
                        Console.WriteLine(payload[j]);
                        payload[j] = false;
                    }
                }
            }
        }
    }
}
";
        const string XunitFactAttributeSource = @"
namespace Xunit
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class FactAttribute : Attribute
    {
        public FactAttribute() { }
    }
}
";

        [Fact]
        public void GotoCoverage()
        {
            string source = @"
using System;

public class Program
{
    public static void Main(string[] args)
    {
        TestMain();
    }

    [Xunit.Fact]
    static void TestMain()
    {
        Console.WriteLine(""foo"");
        goto bar;
        Console.Write(""you won't see me"");
        bar: Console.WriteLine(""bar"");
        Fred();
        return;
    }

    static void Wilma()
    {
        Betty(true);
        Barney(true);
        Barney(false);
        Betty(true);
    }

    static int Barney(bool b)
    {
        if (b)
            return 10;
        if (b)
            return 100;
        return 20;
    }

    static int Betty(bool b)
    {
        if (b)
            return 30;
        if (b)
            return 100;
        return 40;
    }

    static void Fred()
    {
        Wilma();
    }
}
";
            string expectedOutput = @"foo
bar
Flushing
1
True
2
True
True
False
True
True
True
3
True
True
True
True
4
True
True
False
True
True
5
True
True
False
False
False
6
True
";

            string expectedBarneyIL = @"{
  // Code size       82 (0x52)
  .maxstack  4
  .locals init (bool[] V_0)
  IL_0000:  ldsfld     ""bool[][] <PrivateImplementationDetails>.PayloadRoot0""
  IL_0005:  ldtoken    ""int Program.Barney(bool)""
  IL_000a:  ldelem.ref
  IL_000b:  stloc.0
  IL_000c:  ldloc.0
  IL_000d:  brtrue.s   IL_002f
  IL_000f:  ldsfld     ""System.Guid <PrivateImplementationDetails>.MVID""
  IL_0014:  ldtoken    ""int Program.Barney(bool)""
  IL_0019:  ldsfld     ""bool[][] <PrivateImplementationDetails>.PayloadRoot0""
  IL_001e:  ldtoken    ""int Program.Barney(bool)""
  IL_0023:  ldelema    ""bool[]""
  IL_0028:  ldc.i4.5
  IL_0029:  call       ""bool[] Microsoft.CodeAnalysis.Runtime.Instrumentation.CreatePayload(System.Guid, int, ref bool[], int)""
  IL_002e:  stloc.0
  IL_002f:  ldloc.0
  IL_0030:  ldc.i4.1
  IL_0031:  ldc.i4.1
  IL_0032:  stelem.i1
  IL_0033:  ldarg.0
  IL_0034:  brfalse.s  IL_003d
  IL_0036:  ldloc.0
  IL_0037:  ldc.i4.0
  IL_0038:  ldc.i4.1
  IL_0039:  stelem.i1
  IL_003a:  ldc.i4.s   10
  IL_003c:  ret
  IL_003d:  ldloc.0
  IL_003e:  ldc.i4.3
  IL_003f:  ldc.i4.1
  IL_0040:  stelem.i1
  IL_0041:  ldarg.0
  IL_0042:  brfalse.s  IL_004b
  IL_0044:  ldloc.0
  IL_0045:  ldc.i4.2
  IL_0046:  ldc.i4.1
  IL_0047:  stelem.i1
  IL_0048:  ldc.i4.s   100
  IL_004a:  ret
  IL_004b:  ldloc.0
  IL_004c:  ldc.i4.4
  IL_004d:  ldc.i4.1
  IL_004e:  stelem.i1
  IL_004f:  ldc.i4.s   20
  IL_0051:  ret
}
";
            CompilationVerifier verifier = CompileAndVerify(source + InstrumentationHelperSource + XunitFactAttributeSource, emitOptions: EmitOptions.Default.WithInstrument("Test.Flag"), expectedOutput: expectedOutput);
            verifier.VerifyIL("Program.Barney", expectedBarneyIL);
        }

        [Fact]
        public void MethodsOfGenericTypesCoverage()
        {
            string source = @"
using System;

class MyBox<T> where T : class
{
    readonly T _value;

    public MyBox(T value)
    {
        _value = value;
    }

    public T GetValue()
    {
        if (_value == null)
        {
            return null;
        }

        return _value;
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        TestMain();
    }

    [Xunit.Fact]
    static void TestMain()
    {
        MyBox<object> x = new MyBox<object>(null);
        Console.WriteLine(x.GetValue() == null ? ""null"" : x.GetValue().ToString());
        MyBox<string> s = new MyBox<string>(""Hello"");
        Console.WriteLine(s.GetValue() == null ? ""null"" : s.GetValue());
    }
}
";
            // All instrumentation points in method 2 are True because they are covered by at least one specialization.
            //
            // This test verifies that the payloads of methods of generic types are in terms of method definitions and
            // not method references -- the indices for the methods would be different for references.
            string expectedOutput = @"null
Hello
Flushing
1
True
2
True
True
True
3
True
4
True
True
True
True
";

            string expectedGetValueIL = @"{
  // Code size       89 (0x59)
  .maxstack  4
  .locals init (bool[] V_0,
                T V_1)
  IL_0000:  ldsfld     ""bool[][] <PrivateImplementationDetails>.PayloadRoot0""
  IL_0005:  ldtoken    ""T MyBox<T>.GetValue()""
  IL_000a:  ldelem.ref
  IL_000b:  stloc.0
  IL_000c:  ldloc.0
  IL_000d:  brtrue.s   IL_002f
  IL_000f:  ldsfld     ""System.Guid <PrivateImplementationDetails>.MVID""
  IL_0014:  ldtoken    ""T MyBox<T>.GetValue()""
  IL_0019:  ldsfld     ""bool[][] <PrivateImplementationDetails>.PayloadRoot0""
  IL_001e:  ldtoken    ""T MyBox<T>.GetValue()""
  IL_0023:  ldelema    ""bool[]""
  IL_0028:  ldc.i4.3
  IL_0029:  call       ""bool[] Microsoft.CodeAnalysis.Runtime.Instrumentation.CreatePayload(System.Guid, int, ref bool[], int)""
  IL_002e:  stloc.0
  IL_002f:  ldloc.0
  IL_0030:  ldc.i4.1
  IL_0031:  ldc.i4.1
  IL_0032:  stelem.i1
  IL_0033:  ldarg.0
  IL_0034:  ldfld      ""T MyBox<T>._value""
  IL_0039:  box        ""T""
  IL_003e:  brtrue.s   IL_004e
  IL_0040:  ldloc.0
  IL_0041:  ldc.i4.0
  IL_0042:  ldc.i4.1
  IL_0043:  stelem.i1
  IL_0044:  ldloca.s   V_1
  IL_0046:  initobj    ""T""
  IL_004c:  ldloc.1
  IL_004d:  ret
  IL_004e:  ldloc.0
  IL_004f:  ldc.i4.2
  IL_0050:  ldc.i4.1
  IL_0051:  stelem.i1
  IL_0052:  ldarg.0
  IL_0053:  ldfld      ""T MyBox<T>._value""
  IL_0058:  ret
}";
            CompilationVerifier verifier = CompileAndVerify(source + InstrumentationHelperSource + XunitFactAttributeSource, emitOptions: EmitOptions.Default.WithInstrument("Test.Flag"), expectedOutput: expectedOutput);
            verifier.VerifyIL("MyBox<T>.GetValue", expectedGetValueIL);
        }
       
        [Fact]
        public void ImplicitBlockMethodsCoverage()
        {
            string source = @"
using System;

public class Program
{
    public static void Main(string[] args)
    {
        TestMain();
    }
    
    [Xunit.Fact]
    static void TestMain()
    {
        int x = Count;
        x += Prop;
        Prop = x;
        Lambda(x, (y) => y + 1);
    }

    static int Function(int x) => x;

    static int Count => Function(44);

    static int Prop { get; set; }

    static int Lambda(int x, Func<int, int> l)
    {
        return l(x);
    }
}
";
            string expectedOutput = @"Flushing
1
True
2
True
True
True
True
3
True
4
True
5
True
6
True
7
True
";

            CompilationVerifier verifier = CompileAndVerify(source + InstrumentationHelperSource + XunitFactAttributeSource, emitOptions: EmitOptions.Default.WithInstrument("Test.Flag"), expectedOutput: expectedOutput);
        }

        [Fact]
        public void MultipleDeclarationsCoverage()
        {
            string source = @"
using System;

public class Program
{
    public static void Main(string[] args)
    {
        TestMain();
    }

    [Xunit.Fact]
    static void TestMain()
    {
        int x;
        int a, b;
        DoubleDeclaration(5);
        DoubleForDeclaration(5);
    }

    static int DoubleDeclaration(int x)
    {
        int c = x;
        int a, b;
        int f;

        a = b = f = c;
        int d = a, e = b;
        return d + e + f;
    }

    static int DoubleForDeclaration(int x)
    {
        for(int a = x, b = x; a + b < 10; a++)
        {
            x++;
        }

        return x;
    }
}
";
            string expectedOutput = @"Flushing
1
True
2
True
True
3
True
True
True
True
4
True
False
False
True
";

            CompilationVerifier verifier = CompileAndVerify(source + InstrumentationHelperSource + XunitFactAttributeSource, emitOptions: EmitOptions.Default.WithInstrument("Test.Flag"), expectedOutput: expectedOutput);
        }

        [Fact]
        public void UsingAndFixedCoverage()
        {
            string source = @"
using System;
using System.IO;

public class Program
{
    public static void Main(string[] args)
    {
        TestMain();
    }

    [Xunit.Fact]
    static void TestMain()
    {
        using (var memoryStream = new MemoryStream())
        {
            ;
        }

        using (MemoryStream s1 = new MemoryStream(), s2 = new MemoryStream())
        {
            ;
        }

        var otherStream = new MemoryStream();
        using (otherStream)
        {
            ;
        }

        unsafe
        {
            double[] a = { 1, 2, 3 };
            fixed(double* p = a)
            {
                ;
            }
            fixed(double* q = a, r = a)
            {
                ;
            }
        }
    }
}
";
            string expectedOutput = @"Flushing
1
True
2
True
True
True
True
True
True
True
True
True
True
True
True
";
           
            CompilationVerifier verifier = CompileAndVerify(source + InstrumentationHelperSource + XunitFactAttributeSource, options: TestOptions.UnsafeDebugExe,  emitOptions: EmitOptions.Default.WithInstrument("Test.Flag"), expectedOutput: expectedOutput);
        }

        [Fact]
        public void ManyStatementsCoverage()
        {
            string source = @"
using System;

public class Program
{
    public static void Main(string[] args)
    {
        TestMain();
    }


    [Xunit.Fact]
    static void TestMain()
    {
        VariousStatements(2);
    }

    static void VariousStatements(int z)
    {
        int x = z + 10;
        switch (z)
        {
            case 1:
                break;
            case 2:
                break;
            case 3:
                break;
            default:
                break;
        }

        if (x > 10)
        {
            x++;
        }
        else
        {
            x--;
        }

        for (int y = 0; y < 50; y++)
        {
            if (y < 30)
            {
                x++;
                continue;
            }
            else
                break;
        }

        int[] a = new int[] { 1, 2, 3, 4 };
        foreach (int i in a)
        {
            x++;
        }

        while (x < 100)
        {
            x++;
        }

        try
        {
            x++;
            if (x > 10)
            {
                throw new System.Exception();
            }
            x++;
        }
        catch (System.Exception e)
        {
            x++;
        }
        finally
        {
            x++;
        }

        lock (new object())
        {
            ;
        }

        Console.WriteLine(x);

        try
        {
            using ((System.IDisposable)new object())
            {
                ;
            }
        }
        catch (System.Exception e)
        {
        }

        return;
    }
}
";
            string expectedOutput = @"103
Flushing
1
True
2
True
3
True
False
True
False
False
True
True
False
True
True
True
True
True
True
True
True
True
True
True
True
True
True
True
False
True
True
True
True
True
False
True
True
";

            CompileAndVerify(source + InstrumentationHelperSource + XunitFactAttributeSource, emitOptions: EmitOptions.Default.WithInstrument("Test.Flag"), expectedOutput: expectedOutput);
        }

        [Fact]
        public void LambdaCoverage()
        {
            string source = @"
using System;

public class Program
{
    public static void Main(string[] args)
    {
        TestMain();
    }

    [Xunit.Fact]
    static void TestMain()
    {
        int y = 5;
        Func<int, int> tester = (x) =>
        {
            while (x > 10)
            {
                return y;
            }

            return x;
        };

        y = 75;
        if (tester(20) > 50)
            Console.WriteLine(""OK"");
        else
            Console.WriteLine(""Bad"");        
    }
}
";
            string expectedOutput = @"OK
Flushing
1
True
2
True
True
True
False
True
True
True
False
True
";

            CompileAndVerify(source + InstrumentationHelperSource + XunitFactAttributeSource, emitOptions: EmitOptions.Default.WithInstrument("Test.Flag"), expectedOutput: expectedOutput);
        }

        [Fact]
        public void AsyncCoverage()
        {
            string source = @"
using System;
using System.Threading.Tasks;

public class Program
{
    public static void Main(string[] args)
    {
        TestMain();
    }

    [Xunit.Fact]
    static void TestMain()
    {
        Console.WriteLine(Outer(""Goo"").Result);
    }

    async static Task<string> Outer(string s)
    {
        string s1 = await First(s);
        string s2 = await Second(s);

        return s1 + s2;
    }

    async static Task<string> First(string s)
    {
        string result = await Second(s) + ""Glue"";
        if (result.Length > 2)
            return result;
        else
            return ""Too short"";
    }

    async static Task<string> Second(string s)
    {
        string doubled = """";
        if (s.Length > 2)
            doubled = s + s;
        else
            doubled = ""HuhHuh"";
        return await Task.Factory.StartNew(() => doubled);
    }
}
";
            string expectedOutput = @"GooGooGlueGooGoo
Flushing
1
True
2
True
3
True
True
True
4
True
True
False
True
5
True
True
False
True
True
";

            CompileAndVerify(source + InstrumentationHelperSource + XunitFactAttributeSource, emitOptions: EmitOptions.Default.WithInstrument("Test.Flag"), expectedOutput: expectedOutput);
        }

        [Fact]
        public void XunitFactAttribute()
        {
            string source = @"
using System;

public class Program
{
    public static void Main(string[] args)
    {
        MethodWithFactAttribute(1);
        MethodWithOutFactAttribute(1);
        MethodWithFactAttribute(1);
    }

    [Xunit.Fact]
    static int MethodWithFactAttribute(int x)
    {
        var y = x + 1;
        var z = y + 1;
        return z;
    }

    static int MethodWithOutFactAttribute(int x)
    {
        var y = x + 1;
        var z = y + 1;
        return z;
    }
}
";
            string expectedOutput = @"Flushing
1
True
False
False
2
True
True
True
Flushing
1
False
True
True
2
True
True
True
3
True
True
True
";

            string expectedMethodWithFactAttributeIL = @"{
  // Code size       75 (0x4b)
  .maxstack  4
  .locals init (bool[] V_0,
                int V_1)
  .try
  {
    IL_0000:  ldsfld     ""bool[][] <PrivateImplementationDetails>.PayloadRoot0""
    IL_0005:  ldtoken    ""int Program.MethodWithFactAttribute(int)""
    IL_000a:  ldelem.ref
    IL_000b:  stloc.0
    IL_000c:  ldloc.0
    IL_000d:  brtrue.s   IL_002f
    IL_000f:  ldsfld     ""System.Guid <PrivateImplementationDetails>.MVID""
    IL_0014:  ldtoken    ""int Program.MethodWithFactAttribute(int)""
    IL_0019:  ldsfld     ""bool[][] <PrivateImplementationDetails>.PayloadRoot0""
    IL_001e:  ldtoken    ""int Program.MethodWithFactAttribute(int)""
    IL_0023:  ldelema    ""bool[]""
    IL_0028:  ldc.i4.3
    IL_0029:  call       ""bool[] Microsoft.CodeAnalysis.Runtime.Instrumentation.CreatePayload(System.Guid, int, ref bool[], int)""
    IL_002e:  stloc.0
    IL_002f:  ldloc.0
    IL_0030:  ldc.i4.0
    IL_0031:  ldc.i4.1
    IL_0032:  stelem.i1
    IL_0033:  ldarg.0
    IL_0034:  ldc.i4.1
    IL_0035:  add
    IL_0036:  ldloc.0
    IL_0037:  ldc.i4.1
    IL_0038:  ldc.i4.1
    IL_0039:  stelem.i1
    IL_003a:  ldc.i4.1
    IL_003b:  add
    IL_003c:  ldloc.0
    IL_003d:  ldc.i4.2
    IL_003e:  ldc.i4.1
    IL_003f:  stelem.i1
    IL_0040:  stloc.1
    IL_0041:  leave.s    IL_0049
  }
  finally
  {
    IL_0043:  call       ""void Microsoft.CodeAnalysis.Runtime.Instrumentation.FlushPayload()""
    IL_0048:  endfinally
  }
  IL_0049:  ldloc.1
  IL_004a:  ret
}";
            string expectedMethodWithOutFactAttributeIL = @"{
  // Code size       65 (0x41)
  .maxstack  4
  .locals init (bool[] V_0)
  IL_0000:  ldsfld     ""bool[][] <PrivateImplementationDetails>.PayloadRoot0""
  IL_0005:  ldtoken    ""int Program.MethodWithOutFactAttribute(int)""
  IL_000a:  ldelem.ref
  IL_000b:  stloc.0
  IL_000c:  ldloc.0
  IL_000d:  brtrue.s   IL_002f
  IL_000f:  ldsfld     ""System.Guid <PrivateImplementationDetails>.MVID""
  IL_0014:  ldtoken    ""int Program.MethodWithOutFactAttribute(int)""
  IL_0019:  ldsfld     ""bool[][] <PrivateImplementationDetails>.PayloadRoot0""
  IL_001e:  ldtoken    ""int Program.MethodWithOutFactAttribute(int)""
  IL_0023:  ldelema    ""bool[]""
  IL_0028:  ldc.i4.3
  IL_0029:  call       ""bool[] Microsoft.CodeAnalysis.Runtime.Instrumentation.CreatePayload(System.Guid, int, ref bool[], int)""
  IL_002e:  stloc.0
  IL_002f:  ldloc.0
  IL_0030:  ldc.i4.0
  IL_0031:  ldc.i4.1
  IL_0032:  stelem.i1
  IL_0033:  ldarg.0
  IL_0034:  ldc.i4.1
  IL_0035:  add
  IL_0036:  ldloc.0
  IL_0037:  ldc.i4.1
  IL_0038:  ldc.i4.1
  IL_0039:  stelem.i1
  IL_003a:  ldc.i4.1
  IL_003b:  add
  IL_003c:  ldloc.0
  IL_003d:  ldc.i4.2
  IL_003e:  ldc.i4.1
  IL_003f:  stelem.i1
  IL_0040:  ret
}";
            CompilationVerifier verifier = CompileAndVerify(source + InstrumentationHelperSource + XunitFactAttributeSource, emitOptions: EmitOptions.Default.WithInstrument("Test.Flag"), expectedOutput: expectedOutput);
            verifier.VerifyIL("Program.MethodWithFactAttribute", expectedMethodWithFactAttributeIL);
            verifier.VerifyIL("Program.MethodWithOutFactAttribute", expectedMethodWithOutFactAttributeIL);
        }

        private CompilationVerifier CompileAndVerify(string source, EmitOptions emitOptions, string expectedOutput = null, CompilationOptions options = null)
        {
            return base.CompileAndVerify(source, expectedOutput: expectedOutput, additionalRefs: s_asyncRefs, options: options, emitOptions: emitOptions);
        }

        private static readonly MetadataReference[] s_asyncRefs = new[] { MscorlibRef_v4_0_30316_17626, SystemRef_v4_0_30319_17929, SystemCoreRef_v4_0_30319_17929 };
    }
}