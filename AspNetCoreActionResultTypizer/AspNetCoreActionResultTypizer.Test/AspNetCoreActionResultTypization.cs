using System.Threading.Tasks;
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
}