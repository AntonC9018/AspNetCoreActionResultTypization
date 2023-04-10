using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace AspNetCoreActionResultTypizer.Test;

public static class Helper
{
    public static Project CreateDefaultProject()
    {
        var workspace = new AdhocWorkspace();
        var solution = workspace.CurrentSolution;
        var project = solution.AddProject("TestProject", "TestProject", LanguageNames.CSharp);
        var metadataReferences = Helper.GetAllMetadataReferences();
        return project.AddMetadataReferences(metadataReferences);
    }
    public static async Task AssertDiagnostics(Document document, params string[] expectedDiagnostics)
    {
        var semanticModel = await document.GetSemanticModelAsync();
        Assert.NotNull(semanticModel);
        var diagnostics = semanticModel.GetDiagnostics();
        Assert.Equal(expectedDiagnostics, diagnostics, StringToDiagnosticEqualityComparer.Instance);
    }

    private class StringToDiagnosticEqualityComparer : EqualityComparer<object>
    {
        public static readonly StringToDiagnosticEqualityComparer Instance = new();
        
        public override bool Equals(object x, object y)
        {
            if (x is null && y is null) 
                return true;
            if (x is null || y is null) 
                return false;
            if (x is not string id)
                return false;
            if (y is not Diagnostic d)
                return false;
            return d.Id == id;
        }

        public override int GetHashCode(object obj) => obj.GetHashCode();
    }

    private static IEnumerable<MetadataReference> GetReferencesOfType(Type[] types)
    {
        var assemblyPaths = types.Select(t => t.Assembly.Location).Distinct();
        var refs = assemblyPaths.Select(a => MetadataReference.CreateFromFile(a));
        return refs;
    }

    private static IEnumerable<MetadataReference> GetDefaultMetadataReferences()
    {
        var types = new[]
        {
            typeof(object),
            typeof(string), 
            typeof(Task),
        };
        return GetReferencesOfType(types); 
    }

    public static IEnumerable<MetadataReference> GetAllMetadataReferences()
    {
        var defaultMetadataReferences = GetDefaultMetadataReferences();
        var additionalTypes = new[]
        {
            typeof(Controller),
            typeof(ControllerBase),
            typeof(ActionResult),
            typeof(IActionResult)
        };
        var additionalMetadataReferences = GetReferencesOfType(additionalTypes);
        var currentAssembly = Assembly.GetExecutingAssembly()!;
        var referencedAssemblies = currentAssembly.GetReferencedAssemblies();
        var allMetadataReferences = defaultMetadataReferences.Concat(additionalMetadataReferences);
        foreach (var reference in allMetadataReferences)
            yield return reference;
        foreach (var loadedAssembly in referencedAssemblies)
            yield return MetadataReference.CreateFromFile(Assembly.Load(loadedAssembly).Location);
        yield return MetadataReference.CreateFromFile(typeof());
    }
}