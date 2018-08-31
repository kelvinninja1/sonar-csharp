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
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SonarAnalyzer.Common;
using SonarAnalyzer.Helpers;
using SonarAnalyzer.ShimLayer.CSharp;

namespace SonarAnalyzer.Metrics.CSharp
{
    public static class CognitiveComplexityMetric
    {
        public static Dictionary<SyntaxNode, Data> Process(SyntaxTree tree)
        {
            var walker = new CognitiveComplexityWalker();

            try
            {
                walker.Visit(tree.GetRoot());
            }
            catch (InsufficientExecutionStackException)
            {
                // TODO: trace this exception

                // Roslyn walker overflows the stack when the depth of the call is around 2050.
                // See ticket #727.
            }

            // TODO: Ensure the nesting levels are 0 at the end of the process

            return walker.complexityPerMember;
        }

        public class Data
        {
            public int Complexity { get; set; }
            public List<SecondaryLocation> IncrementLocations { get; set; } = new List<SecondaryLocation>();
        }

        private class CognitiveComplexityWalker : CSharpSyntaxWalker
        {
            private static readonly ISet<SyntaxKind> SyntaxKindCausingGrouping =
                new HashSet<SyntaxKind>
                {
                    SyntaxKind.FieldDeclaration,
                    SyntaxKind.MethodDeclaration,
                    SyntaxKind.ConstructorDeclaration,
                    SyntaxKind.DestructorDeclaration,
                    SyntaxKind.OperatorDeclaration,
                    SyntaxKind.GetAccessorDeclaration,
                    SyntaxKind.SetAccessorDeclaration,
                    SyntaxKind.AddAccessorDeclaration,
                    SyntaxKind.RemoveAccessorDeclaration
                };

            public readonly Dictionary<SyntaxNode, Data> complexityPerMember = new Dictionary<SyntaxNode, Data>();
            private readonly Stack<Data> complexityPerDirectiveLevel = new Stack<Data>();
            private readonly List<ExpressionSyntax> logicalOperationsToIgnore = new List<ExpressionSyntax>();
            private readonly Queue<Action> queuedActions = new Queue<Action>();

            public int memberNestingLevel;
            public int directivesNestingLevel;
            private Data memberComplexity = new Data();
            private bool needsToQueueActions;
            private SyntaxNode currentNode;
            private MethodDeclarationSyntax currentMethod;
            private bool hasDirectRecursiveCall;

            public CognitiveComplexityWalker()
                : base(SyntaxWalkerDepth.StructuredTrivia)
            {
            }

            #region Visit Directives
            public override void VisitIfDirectiveTrivia(IfDirectiveTriviaSyntax node)
            {
                this.complexityPerDirectiveLevel.Push(new Data());
                IncreaseComplexityByNestingPlusOne(node.IfKeyword, isCompilerDirective: true);
                this.directivesNestingLevel++;
                DequeueAllActions();
                base.VisitIfDirectiveTrivia(node);
            }

            public override void VisitElifDirectiveTrivia(ElifDirectiveTriviaSyntax node)
            {
                IncreaseComplexityByOne(node.ElifKeyword, isCompilerDirective: true);
                DequeueAllActions();
                base.VisitElifDirectiveTrivia(node);
            }

            public override void VisitElseDirectiveTrivia(ElseDirectiveTriviaSyntax node)
            {
                IncreaseComplexityByOne(node.ElseKeyword, isCompilerDirective: true);
                DequeueAllActions();
                base.VisitElseDirectiveTrivia(node);
            }

            public override void VisitEndIfDirectiveTrivia(EndIfDirectiveTriviaSyntax node)
            {
                // Remove complexity data related to the #if/#elif/#else block ending with this #endif
                if (this.currentNode != null)
                {
                    var directiveLevelData = this.complexityPerDirectiveLevel.Pop();
                    this.complexityPerMember[this.currentNode].Complexity += directiveLevelData.Complexity;
                    this.complexityPerMember[this.currentNode].IncrementLocations.AddRange(directiveLevelData.IncrementLocations);
                }

                this.directivesNestingLevel--;
                DequeueAllActions();
                base.VisitEndIfDirectiveTrivia(node);
            }
            #endregion // Visit Directives

            #region Other Visit
            public override void Visit(SyntaxNode node)
            {
                this.needsToQueueActions |= node.ContainsDirectives;

                if (node.IsKind(SyntaxKindEx.LocalFunctionStatement))
                {
                    VisitWithNesting(node, base.Visit);
                }
                else if (node.IsAnyKind(SyntaxKindCausingGrouping))
                {
                    this.complexityPerMember.Add(node, new Data());
                    this.currentNode = node;

                    base.Visit(node);

                    DequeueAllActions();
                    // When we have finished exploring the member, save the related complexity info
                    this.complexityPerMember[node].Complexity += this.memberComplexity.Complexity
                        + this.complexityPerDirectiveLevel.Sum(x => x.Complexity);
                    this.complexityPerMember[node].IncrementLocations = this.complexityPerMember[node].IncrementLocations
                        .Concat(this.complexityPerDirectiveLevel.SelectMany(x => x.IncrementLocations))
                        .Concat(this.memberComplexity.IncrementLocations)
                        .ToList();

                    // reset member values
                    this.memberComplexity = new Data();
                    this.memberNestingLevel = 0;
                    this.currentNode = null;
                }
                else
                {
                    base.Visit(node);
                }
            }

            public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
            {
                this.currentMethod = node;
                base.VisitMethodDeclaration(node);

                if (this.hasDirectRecursiveCall)
                {
                    IncreaseComplexity(node.Identifier, () => 1, () => "+1 (recursion)");
                    this.hasDirectRecursiveCall = false;
                }
            }

            public override void VisitIfStatement(IfStatementSyntax node)
            {
                if (node.Parent.IsKind(SyntaxKind.ElseClause))
                {
                    base.VisitIfStatement(node);
                }
                else
                {
                    IncreaseComplexityByNestingPlusOne(node.IfKeyword);
                    VisitWithNesting(node, base.VisitIfStatement);
                }
            }

            public override void VisitElseClause(ElseClauseSyntax node)
            {
                IncreaseComplexityByOne(node.ElseKeyword);
                base.VisitElseClause(node);
            }

            public override void VisitConditionalExpression(ConditionalExpressionSyntax node)
            {
                IncreaseComplexityByNestingPlusOne(node.QuestionToken);
                VisitWithNesting(node, base.VisitConditionalExpression);
            }

            public override void VisitSwitchStatement(SwitchStatementSyntax node)
            {
                IncreaseComplexityByNestingPlusOne(node.SwitchKeyword);
                VisitWithNesting(node, base.VisitSwitchStatement);
            }

            public override void VisitForStatement(ForStatementSyntax node)
            {
                IncreaseComplexityByNestingPlusOne(node.ForKeyword);
                VisitWithNesting(node, base.VisitForStatement);
            }

            public override void VisitWhileStatement(WhileStatementSyntax node)
            {
                IncreaseComplexityByNestingPlusOne(node.WhileKeyword);
                VisitWithNesting(node, base.VisitWhileStatement);
            }

            public override void VisitDoStatement(DoStatementSyntax node)
            {
                IncreaseComplexityByNestingPlusOne(node.DoKeyword);
                VisitWithNesting(node, base.VisitDoStatement);
            }

            public override void VisitForEachStatement(ForEachStatementSyntax node)
            {
                IncreaseComplexityByNestingPlusOne(node.ForEachKeyword);
                VisitWithNesting(node, base.VisitForEachStatement);
            }

            public override void VisitCatchClause(CatchClauseSyntax node)
            {
                IncreaseComplexityByNestingPlusOne(node.CatchKeyword);
                VisitWithNesting(node, base.VisitCatchClause);
            }

            public override void VisitInvocationExpression(InvocationExpressionSyntax node)
            {
                var identifierNameSyntax = node.Expression as IdentifierNameSyntax;
                if (this.currentMethod != null &&
                    identifierNameSyntax != null &&
                    node.HasExactlyNArguments(this.currentMethod.ParameterList.Parameters.Count) &&
                    string.Equals(identifierNameSyntax.Identifier.ValueText,
                        this.currentMethod.Identifier.ValueText, StringComparison.Ordinal))
                {
                    this.hasDirectRecursiveCall = true;
                }

                base.VisitInvocationExpression(node);
            }

            public override void VisitBinaryExpression(BinaryExpressionSyntax node)
            {
                var nodeKind = node.Kind();
                if (!this.logicalOperationsToIgnore.Contains(node) &&
                    (nodeKind == SyntaxKind.LogicalAndExpression ||
                     nodeKind == SyntaxKind.LogicalOrExpression))
                {
                    var left = node.Left.RemoveParentheses();
                    if (!left.IsKind(nodeKind))
                    {
                        IncreaseComplexityByOne(node.OperatorToken);
                    }

                    var right = node.Right.RemoveParentheses();
                    if (right.IsKind(nodeKind))
                    {
                        this.logicalOperationsToIgnore.Add(right);
                    }
                }

                base.VisitBinaryExpression(node);
            }

            public override void VisitGotoStatement(GotoStatementSyntax node)
            {
                IncreaseComplexityByNestingPlusOne(node.GotoKeyword);
                base.VisitGotoStatement(node);
            }

            public override void VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node)
            {
                VisitWithNesting(node, base.VisitSimpleLambdaExpression);
            }

            public override void VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node)
            {
                VisitWithNesting(node, base.VisitParenthesizedLambdaExpression);
            }
            #endregion

            #region Helpers
            private void VisitWithNesting<TSyntaxNode>(TSyntaxNode node, Action<TSyntaxNode> visit)
            {
                DoOrQueueAction(() => this.memberNestingLevel++);
                visit(node);
                DoOrQueueAction(() => this.memberNestingLevel--);
            }

            private void IncreaseComplexityByOne(SyntaxToken token, bool isCompilerDirective = false)
            {
                IncreaseComplexity(token, () => 1, () => "+1", isCompilerDirective);
            }

            private void IncreaseComplexityByNestingPlusOne(SyntaxToken token, bool isCompilerDirective = false)
            {
                Func<int> getIncrement = () => this.directivesNestingLevel + this.memberNestingLevel + 1;
                Func<string> getMessage = () =>
                {
                    var increment = getIncrement();
                    return increment == 1
                        ? "+1"
                        : $"+{increment} (incl {increment - 1} for nesting)";
                };
                IncreaseComplexity(token, getIncrement, getMessage, isCompilerDirective);
            }

            private void IncreaseComplexity(SyntaxToken token, Func<int> getIncrement, Func<string> getMessage,
                bool isCompilerDirective = false)
            {
                if (isCompilerDirective)
                {
                    var complexityData = this.complexityPerDirectiveLevel.Peek();
                    complexityData.Complexity += getIncrement();
                    complexityData.IncrementLocations.Add(new SecondaryLocation(token.GetLocation(), getMessage()));
                }
                else
                {
                    DoOrQueueAction(() =>
                    {
                        this.memberComplexity.Complexity += getIncrement();
                        this.memberComplexity.IncrementLocations.Add(new SecondaryLocation(token.GetLocation(), getMessage()));
                    });
                }
            }

            private void DoOrQueueAction(Action action)
            {
                if (this.needsToQueueActions)
                {
                    this.queuedActions.Enqueue(action);
                }
                else
                {
                    action();
                }
            }

            private void DequeueAllActions()
            {
                while (this.queuedActions.Count > 0)
                {
                    this.queuedActions.Dequeue().Invoke();
                }

                this.needsToQueueActions = false;
            }
            #endregion
        }
    }
}
