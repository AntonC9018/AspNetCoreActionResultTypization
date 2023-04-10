using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
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
    public async Task VarStringDeclarationCouldBeConstant_Diagnostic()
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