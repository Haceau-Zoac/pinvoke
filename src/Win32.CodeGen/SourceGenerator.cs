﻿// Copyright © .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Win32.CodeGen
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Text;
    using System.Text.Json;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.Text;

    [Generator]
    public class SourceGenerator : ISourceGenerator
    {
        private const string NativeMethodsTxtAdditionalFileName = "NativeMethods.txt";
        private const string NativeMethodsJsonAdditionalFileName = "NativeMethods.json";
        private static readonly DiagnosticDescriptor NoMatchingMethodOrType = new DiagnosticDescriptor(
            "PInvoke001",
            "No matching method or type found",
            "Method or type \"{0}\" not found.",
            "Functionality",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor NoMethodsForModule = new DiagnosticDescriptor(
            "PInvoke001",
            "No module found",
            "No methods found under module \"{0}\".",
            "Functionality",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public void Initialize(GeneratorInitializationContext context)
        {
        }

        public void Execute(GeneratorExecutionContext context)
        {
            if (!(context.Compilation is CSharpCompilation))
            {
                return;
            }

            GeneratorOptions? options = null;
            AdditionalText? nativeMethodsJsonFile = context.AdditionalFiles
                .FirstOrDefault(af => string.Equals(Path.GetFileName(af.Path), NativeMethodsJsonAdditionalFileName, StringComparison.OrdinalIgnoreCase));
            if (nativeMethodsJsonFile is object)
            {
                string optionsJson = nativeMethodsJsonFile.GetText(context.CancellationToken)!.ToString();
                options = JsonSerializer.Deserialize<GeneratorOptions>(optionsJson, new JsonSerializerOptions
                {
                    AllowTrailingCommas = true,
                    ReadCommentHandling = JsonCommentHandling.Skip,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                });
            }

            AdditionalText? nativeMethodsTxtFile = context.AdditionalFiles
                .FirstOrDefault(af => string.Equals(Path.GetFileName(af.Path), NativeMethodsTxtAdditionalFileName, StringComparison.OrdinalIgnoreCase));
            if (nativeMethodsTxtFile is null)
            {
                return;
            }

            var generator = new Generator(options, (CSharpCompilation)context.Compilation, (CSharpParseOptions)context.ParseOptions);

            SourceText? nativeMethodsTxt = nativeMethodsTxtFile.GetText(context.CancellationToken);
            if (nativeMethodsTxt is null)
            {
                return;
            }

            foreach (TextLine line in nativeMethodsTxt.Lines)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                string name = line.ToString();
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var location = Location.Create(nativeMethodsTxtFile.Path, line.Span, nativeMethodsTxt.Lines.GetLinePositionSpan(line.Span));
                if (name.EndsWith(".*"))
                {
                    var moduleName = name.Substring(0, name.Length - 2);
                    if (!generator.TryGenerateAllExternMethods(moduleName, context.CancellationToken))
                    {
                        context.ReportDiagnostic(Diagnostic.Create(NoMethodsForModule, location, moduleName));
                    }
                }
                else
                {
                    if (!generator.TryGenerateExternMethod(name) &&
                        !generator.TryGenerateType(name) &&
                        !generator.TryGenerateConstant(name))
                    {
                        context.ReportDiagnostic(Diagnostic.Create(NoMatchingMethodOrType, location, name));
                    }
                }
            }

            var compilationUnits = generator.GetCompilationUnits(context.CancellationToken);
            foreach (var unit in compilationUnits)
            {
                context.AddSource(unit.Key, unit.Value.ToFullString());
            }
        }
    }
}
