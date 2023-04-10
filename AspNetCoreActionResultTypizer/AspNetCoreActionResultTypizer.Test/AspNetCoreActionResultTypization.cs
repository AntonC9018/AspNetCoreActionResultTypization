using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AspNetCoreActionResultTypizer.Test;

using VerifyCS = CSharpCodeFixVerifier<AspNetCoreActionResultTypizerAnalyzer, AspNetCoreActionResultTypizerCodeFixProvider>;


[TestClass]
public class AspNetCoreActionResultTypizerUnitTest
{
    public void Setup()
    {
        
    }
    
    [TestMethod]
    public async Task VarStringDeclarationCouldBeConstant_Diagnostic()
    {
        await VerifyCS.VerifyAnalyzerAsync("""
            using Microsoft.AspNetCore.Mvc;
            public class MyController : Controller
            {
            }
        """);
    }
}