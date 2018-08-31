/*
 * SonarAnalyzer for .NET
 * Copyright (C) 2015-2018 SonarSource SA
 * mailto: contact AT sonarsource DOT com
 *
 * This program is free software; you can redistribute it and/or
 * modify it under the terms of the GNU Lesser General Public
 * License as published by the Free Software Foundation; either
 * version 3 of the License, or (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
 * Lesser General Public License for more details.
 *
 * You should have received a copy of the GNU Lesser General Public License
 * along with this program; if not, write to the Free Software Foundation,
 * Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.
 */

using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using SonarAnalyzer.Common;
using SonarAnalyzer.Helpers;
using SonarAnalyzer.Metrics.CSharp;

namespace SonarAnalyzer.Rules.CSharp
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    [Rule(DiagnosticId)]
    public class CognitiveComplexityShouldNotBeTooHigh : ParameterLoadingDiagnosticAnalyzer
    {
        internal const string DiagnosticId = "S3776";
        private const string MessageFormat = "Refactor this {0} to reduce its Cognitive Complexity from {1} to the {2} allowed.";
        private const int DefaultThreshold = 15;
        private const int DefaultPropertyThreshold = 3;

        [RuleParameter("threshold", PropertyType.Integer, "The maximum authorized complexity.", DefaultThreshold)]
        public int Threshold { get; set; } = DefaultThreshold;

        [RuleParameter("propertyThreshold ", PropertyType.Integer, "The maximum authorized complexity in a property.", DefaultPropertyThreshold)]
        public int PropertyThreshold { get; set; } = DefaultPropertyThreshold;

        private static readonly DiagnosticDescriptor rule =
            DiagnosticDescriptorBuilder.GetDescriptor(DiagnosticId, MessageFormat, RspecStrings.ResourceManager,
                isEnabledByDefault: false);
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(rule);

        protected override void Initialize(ParameterLoadingAnalysisContext context)
        {
            context.RegisterSyntaxTreeActionInNonGenerated(
                c =>
                {
                    foreach (var group in CognitiveComplexityMetric.Process(c.Tree))
                    {
                        if (group.Value.Complexity > Threshold)
                        {
                            var elements = GetElements(group.Key);
                            if (elements != null)
                            {
                                c.ReportDiagnosticWhenActive(Diagnostic.Create(rule, elements.Item2,
                                    group.Value.IncrementLocations.ToAdditionalLocations(),
                                    group.Value.IncrementLocations.ToProperties(),
                                    new object[] { elements.Item1, group.Value.Complexity, elements.Item3 }));
                            }
                        }
                    }
                });
        }

        private Tuple<string, Location, int> GetElements(SyntaxNode node)
        {
            switch (node)
            {
                case FieldDeclarationSyntax fieldDeclaration:
                    return new Tuple<string, Location, int>("field", fieldDeclaration.Declaration.GetLocation(), PropertyThreshold);

                case MethodDeclarationSyntax methodDeclaration:
                    return new Tuple<string, Location, int>("method", methodDeclaration.Identifier.GetLocation(), Threshold);

                case ConstructorDeclarationSyntax constructorDeclaration:
                    return new Tuple<string, Location, int>("constructor", constructorDeclaration.Identifier.GetLocation(), Threshold);

                case DestructorDeclarationSyntax destructorDeclaration:
                    return new Tuple<string, Location, int>("destructor", destructorDeclaration.Identifier.GetLocation(), Threshold);

                case OperatorDeclarationSyntax operatorDeclaration:
                    return new Tuple<string, Location, int>("operator", operatorDeclaration.OperatorToken.GetLocation(), Threshold);

                case PropertyDeclarationSyntax propertyDeclaration:
                    return new Tuple<string, Location, int>("property", propertyDeclaration.Identifier.GetLocation(), PropertyThreshold);

                case AccessorDeclarationSyntax accessorDeclaration:
                    return new Tuple<string, Location, int>("accessor", accessorDeclaration.Keyword.GetLocation(), PropertyThreshold);

                default:
                    return null;
            }
        }
    }
}
