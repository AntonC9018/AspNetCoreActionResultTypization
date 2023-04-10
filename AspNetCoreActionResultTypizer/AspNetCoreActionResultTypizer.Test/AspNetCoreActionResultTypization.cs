using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Xunit;

namespace AspNetCoreActionResultTypizer.Test;

public class AspNetCoreActionResultTypizerUnitTest
{
    private Project _project;
    
    public AspNetCoreActionResultTypizerUnitTest()
    {
        _project = Helper.CreateDefaultProject();
    }

    [Fact]
    public async Task IfActionReturnsIActionResult_DiagnosticIsGenerated()
    {
        var document = _project.AddDocument("TestDocument", """
            using Microsoft.AspNetCore.Mvc;
            public class MyController : Controller
            {
                public IActionResult MyAction()
                {
                    var x = "Hello World";
                    return Ok(x);
                }
            }
        """);
        await Helper.AssertDiagnostics(document, AspNetCoreActionResultTypizerAnalyzer.DiagnosticId);
    }

    [Fact]
    public async Task IfActionReturnsTaskOfIActionResult_DiagnosticIsGenerated()
    {
        var document = _project.AddDocument("TestDocument", """
            using Microsoft.AspNetCore.Mvc;
            public class MyController : Controller
            {
                public IActionResult MyAction()
                {
                    var x = "Hello World";
                    return Ok(x);
                }
            }
        """);
        await Helper.AssertDiagnostics(document, AspNetCoreActionResultTypizerAnalyzer.DiagnosticId);
    }
}