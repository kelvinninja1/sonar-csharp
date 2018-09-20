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
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;
using SonarAnalyzer.Common;
using SonarAnalyzer.Helpers;
using SonarAnalyzer.Metrics.VisualBasic;

namespace SonarAnalyzer.Rules.VisualBasic
{
    [DiagnosticAnalyzer(LanguageNames.VisualBasic)]
    [Rule(DiagnosticId)]
    public sealed class CognitiveComplexity : CognitiveComplexityBase
    {
        private static readonly DiagnosticDescriptor rule =
             DiagnosticDescriptorBuilder.GetDescriptor(DiagnosticId, MessageFormat, RspecStrings.ResourceManager,
                 isEnabledByDefault: false);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(rule);

        protected override void Initialize(ParameterLoadingAnalysisContext context)
        {
           context.RegisterSyntaxNodeActionInNonGenerated(
                c => CheckComplexity<MethodBlockSyntax>(c,
                    m => m,
                    m => m.EndSubOrFunctionStatement.BlockKeyword.GetLocation(),
                    "sub",
                    Threshold),
                SyntaxKind.SubBlock,
                SyntaxKind.FunctionBlock);

            context.RegisterSyntaxNodeActionInNonGenerated(
                c => CheckComplexity<ConstructorBlockSyntax>(c,
                    co => co,
                    co => co.BlockStatement.DeclarationKeyword.GetLocation(),
                    "constructor",
                    Threshold),
                SyntaxKind.ConstructorBlock);

            context.RegisterSyntaxNodeActionInNonGenerated(
                c => CheckComplexity<OperatorBlockSyntax>(c,
                    o => o,
                    o => o.BlockStatement.DeclarationKeyword.GetLocation(),
                    "operator",
                    Threshold),
                SyntaxKind.OperatorBlock);

            context.RegisterSyntaxNodeActionInNonGenerated(
                c => CheckComplexity<AccessorBlockSyntax>(c,
                    a => a,
                    a => a.AccessorStatement.DeclarationKeyword.GetLocation(),
                    "accessor",
                    PropertyThreshold),
                SyntaxKind.GetAccessorBlock,
                SyntaxKind.SetAccessorBlock);

            context.RegisterSyntaxNodeActionInNonGenerated(
               c => CheckComplexity<FieldDeclarationSyntax>(c,
                    m => m,
                    m => m.Declarators[0].Names[0].Identifier.GetLocation(),
                    "field", Threshold),
               SyntaxKind.FieldDeclaration);
        }

        protected void CheckComplexity<TSyntax>(SyntaxNodeAnalysisContext context,
                Func<TSyntax, SyntaxNode> nodeSelector,
                Func<TSyntax, Location> getLocationToReport,
                string declarationType,
                int threshold)
            where TSyntax : SyntaxNode
        {
            var syntax = (TSyntax)context.Node;
            var nodeToAnalyze = nodeSelector(syntax);
            if (nodeToAnalyze == null)
            {
                return;
            }

            var cognitiveWalker = new CognitiveComplexityWalker();
            cognitiveWalker.Walk(nodeToAnalyze);
            cognitiveWalker.EnsureVisitEndedCorrectly();

            if (cognitiveWalker.Complexity > Threshold)
            {
                context.ReportDiagnosticWhenActive(Diagnostic.Create(rule, getLocationToReport(syntax),
                    cognitiveWalker.IncrementLocations.ToAdditionalLocations(),
                    cognitiveWalker.IncrementLocations.ToProperties(),
                    new object[] { declarationType, cognitiveWalker.Complexity, threshold }));
            }
        }
    }
}
