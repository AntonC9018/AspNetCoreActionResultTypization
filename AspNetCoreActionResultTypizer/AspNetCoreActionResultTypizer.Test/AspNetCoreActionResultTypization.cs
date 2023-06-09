﻿using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace AspNetCoreActionResultTypizer.Test;

using VerifyCS = CSharpCodeFixVerifier<AspNetCoreActionResultTypizerAnalyzer, AspNetCoreActionResultTypizerCodeFixProvider>;

public class AspNetCoreActionResultTypizerUnitTest
{
    private Project _project;
    
    public AspNetCoreActionResultTypizerUnitTest()
    {
        _project = Helper.CreateDefaultProject();
    }

    private static readonly DiagnosticResult AnalyzerDiagnostic =
        new(AspNetCoreActionResultTypizerAnalyzer.Rule);
    [Fact]
    public async Task IfActionReturnsIActionResult_DiagnosticIsGenerated()
    {
        await VerifyCS.VerifyAnalyzerAsync("""
            using Microsoft.AspNetCore.Mvc;
            public class MyController : Controller
            {
                public IActionResult MyAction()
                {
                    var x = "Hello World";
                    return Ok(x);
                }
            }
        """, AnalyzerDiagnostic.WithSpan(4, 16, 4, 29));
    }

    [Fact]
    public async Task IfActionReturnsTaskOfIActionResult_DiagnosticIsGenerated()
    {
        await VerifyCS.VerifyAnalyzerAsync("""
            using Microsoft.AspNetCore.Mvc;
            using System.Threading.Tasks;
            public class MyController : Controller
            {
                public async Task<IActionResult> MyAction()
                {
                    var x = "Hello World";
                    return Ok(x);
                }
            }
        """, AnalyzerDiagnostic.WithSpan(5,22, 5, 41));
    }

    [Fact]
    public async Task OkThatsNotTheBaseControllersOk_IsIgnored()
    {
        await VerifyCS.VerifyAnalyzerAsync("""
            using Microsoft.AspNetCore.Mvc;
            public class MyController : Controller
            {
                public IActionResult MyAction()
                {
                    IActionResult Ok(object x) => this.Ok(x);  
                    var x = "Hello World";
                    return Ok(x);
                }
            }
        """);
    }

    [Fact]
    public async Task CodeFixWorksForSimpleType_NotAsync()
    {
        await VerifyCS.VerifyCodeFixAsync("""
            using Microsoft.AspNetCore.Mvc;
            public class MyController : Controller
            {
                public IActionResult MyAction()
                {
                    var x = "Hello World";
                    return Ok(x);
                }
            }
        """, AnalyzerDiagnostic.WithSpan(4, 16, 4, 29),
            """
            using Microsoft.AspNetCore.Mvc;
            public class MyController : Controller
            {
                public ActionResult<string> MyAction()
                {
                    var x = "Hello World";
                    return Ok(x);
                }
            }
        """);
    }
    
    [Fact]
    public async Task CodeFixWorksForSimpleType_Async()
    {
        await VerifyCS.VerifyCodeFixAsync("""
            using Microsoft.AspNetCore.Mvc;
            using System.Threading.Tasks;
            public class MyController : Controller
            {
                public async Task<IActionResult> MyAction()
                {
                    var x = "Hello World";
                    return Ok(x);
                }
            }
        """, AnalyzerDiagnostic.WithSpan(5, 22, 5, 41),
            """
            using Microsoft.AspNetCore.Mvc;
            using System.Threading.Tasks;
            public class MyController : Controller
            {
                public async Task<ActionResult<string>> MyAction()
                {
                    var x = "Hello World";
                    return Ok(x);
                }
            }
        """);
    }
    
    [Fact]
    public async Task CodeFixWorksForGenericType_NotAsync()
    {
        await VerifyCS.VerifyCodeFixAsync("""
            using Microsoft.AspNetCore.Mvc;
            public class GenericType<T>
            {
                public T Value { get; set; }
            } 
            public class MyController : Controller
            {
                public IActionResult MyAction()
                {
                    var x = new GenericType<string> { Value = "Hello World" };
                    return Ok(x);
                }
            }
        """, AnalyzerDiagnostic.WithSpan(8, 16, 8, 29),
            """
            using Microsoft.AspNetCore.Mvc;
            public class GenericType<T>
            {
                public T Value { get; set; }
            } 
            public class MyController : Controller
            {
                public ActionResult<GenericType<string>> MyAction()
                {
                    var x = new GenericType<string> { Value = "Hello World" };
                    return Ok(x);
                }
            }
        """);
    }
    
    [Fact]
    public async Task CodeFixWorksForGenericType_Async()
    {
        await VerifyCS.VerifyCodeFixAsync("""
            using Microsoft.AspNetCore.Mvc;
            using System.Threading.Tasks;
            public class GenericType<T>
            {
                public T Value { get; set; }
            } 
            public class MyController : Controller
            {
                public async Task<IActionResult> MyAction()
                {
                    var x = new GenericType<string> { Value = "Hello World" };
                    return Ok(x);
                }
            }
        """, AnalyzerDiagnostic.WithSpan(9, 22, 9, 41),
            """
            using Microsoft.AspNetCore.Mvc;
            using System.Threading.Tasks;
            public class GenericType<T>
            {
                public T Value { get; set; }
            } 
            public class MyController : Controller
            {
                public async Task<ActionResult<GenericType<string>>> MyAction()
                {
                    var x = new GenericType<string> { Value = "Hello World" };
                    return Ok(x);
                }
            }
        """);
    }

    [Fact]
    public async Task MultipleActionsGetFixedAtOnce()
    {
        await VerifyCS.VerifyCodeFixAsync("""
            using Microsoft.AspNetCore.Mvc;
            public class MyController : Controller
            {
                public IActionResult A()
                {
                    return Ok("String");
                }

                public IActionResult B()
                {
                    return Ok(123);
                }
            }
        """, new[]
            { 
                AnalyzerDiagnostic.WithSpan(4, 16, 4, 29),
                AnalyzerDiagnostic.WithSpan(9, 16, 9, 29),
            },
            """
            using Microsoft.AspNetCore.Mvc;
            public class MyController : Controller
            {
                public ActionResult<string> A()
                {
                    return Ok("String");
                }

                public ActionResult<int> B()
                {
                    return Ok(123);
                }
            }
        """);
    }
}
