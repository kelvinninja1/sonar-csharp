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
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;
using SonarAnalyzer.Common;
using SonarAnalyzer.Helpers.VisualBasic;

namespace SonarAnalyzer.Metrics.VisualBasic
{
    public sealed class CognitiveComplexityWalker : VisualBasicSyntaxWalker
    {
        private readonly List<SecondaryLocation> incrementLocations = new List<SecondaryLocation>();
        private readonly List<ExpressionSyntax> logicalOperationsToIgnore = new List<ExpressionSyntax>();

        private MethodStatementSyntax currentMethod;
        private int nestingLevel;
        private bool hasDirectRecursiveCall;

        public int Complexity { get; private set; }

        public bool VisitEndedCorrectly => this.nestingLevel == 0;

        public IEnumerable<SecondaryLocation> IncrementLocations => this.incrementLocations;

        public void EnsureVisitEndedCorrectly()
        {
            if (!VisitEndedCorrectly)
            {
                throw new InvalidOperationException("There is a problem with the cognitive complexity walker. " +
                    $"Expecting ending nesting to be '0' got '{this.nestingLevel}'");
            }
        }

        public void Walk(SyntaxNode node)
        {
            try
            {
                Visit(node);
            }
            catch (InsufficientExecutionStackException)
            {
                // TODO: trace this exception

                // Roslyn walker overflows the stack when the depth of the call is around 2050.
                // See ticket #727.

                // Reset nesting level, so the problem with the walker is not reported.
                this.nestingLevel = 0;
            }
        }

        /**
        public override void Visit(SyntaxNode node)
        {
            // TODO fix if needed
            if (false)
            //if (node.IsKind(SyntaxKindEx.LocalFunctionStatement))
            {
                VisitWithNesting(node, base.Visit);
            }
            else
            {
                base.Visit(node);
            }
        }
    */

        public override void VisitMethodStatement(MethodStatementSyntax node)
        {
            this.currentMethod = node;
            base.VisitMethodStatement(node);

            if (this.hasDirectRecursiveCall)
            {
                IncreaseComplexity(node.Identifier, 1, "+1 (recursion)");
            }
        }

        // FIXME add single line if!

        public override void VisitMultiLineIfBlock(MultiLineIfBlockSyntax node)
        {
            IncreaseComplexityByNestingPlusOne(node.IfStatement.IfKeyword);
            VisitWithNesting(node, base.VisitMultiLineIfBlock);
        }

        public override void VisitElseIfStatement(ElseIfStatementSyntax node)
        {
            IncreaseComplexityByOne(node.ElseIfKeyword);
            base.VisitElseIfStatement(node);
        }

        public override void VisitElseStatement(ElseStatementSyntax node)
        {
            IncreaseComplexityByOne(node.ElseKeyword);
            base.VisitElseStatement(node);
        }

        public override void VisitBinaryConditionalExpression(BinaryConditionalExpressionSyntax node)
        {
            IncreaseComplexityByNestingPlusOne(node.IfKeyword);
            VisitWithNesting(node, base.VisitBinaryConditionalExpression);
        }

        public override void VisitTernaryConditionalExpression(TernaryConditionalExpressionSyntax node)
        {
            IncreaseComplexityByNestingPlusOne(node.IfKeyword);
            VisitWithNesting(node, base.VisitTernaryConditionalExpression);
        }

        public override void VisitSelectBlock(SelectBlockSyntax node)
        {
            IncreaseComplexityByNestingPlusOne(node.SelectStatement.SelectKeyword);
            VisitWithNesting(node, base.VisitSelectBlock);
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
            IncreaseComplexityByNestingPlusOne(node.ForKeyword);
            VisitWithNesting(node, base.VisitForEachStatement);
        }

        public override void VisitCatchBlock(CatchBlockSyntax node)
        {
            IncreaseComplexityByNestingPlusOne(node.CatchStatement.CatchKeyword);
            VisitWithNesting(node, base.VisitCatchBlock);
        }

        public override void VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            if (node.Expression == null || node.ArgumentList == null)
            {
                return;
            }
            var identifierNameSyntax = node.Expression as IdentifierNameSyntax;
            if (identifierNameSyntax == null)
            {
                return;
            }
            if (this.currentMethod != null &&
                node.ArgumentList.Arguments.Count == this.currentMethod.ParameterList.Parameters.Count &&
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
                (nodeKind == SyntaxKind.AndExpression ||
                 nodeKind == SyntaxKind.AndAlsoExpression ||
                 nodeKind == SyntaxKind.OrExpression ||
                 nodeKind == SyntaxKind.OrElseExpression))
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

        public override void VisitGoToStatement(GoToStatementSyntax node)
        {
            IncreaseComplexityByNestingPlusOne(node.GoToKeyword);
            base.VisitGoToStatement(node);
        }

        public override void VisitSingleLineLambdaExpression(SingleLineLambdaExpressionSyntax node)
        {
            VisitWithNesting(node, base.VisitSingleLineLambdaExpression);
        }

        public override void VisitMultiLineLambdaExpression(MultiLineLambdaExpressionSyntax node)
        {
            VisitWithNesting(node, base.VisitMultiLineLambdaExpression);
        }

        private void VisitWithNesting<TSyntaxNode>(TSyntaxNode node, Action<TSyntaxNode> visit)
        {
            this.nestingLevel++;
            visit(node);
            this.nestingLevel--;
        }

        private void IncreaseComplexityByOne(SyntaxToken token)
        {
            IncreaseComplexity(token, 1, "+1");
        }

        private void IncreaseComplexityByNestingPlusOne(SyntaxToken token)
        {
            var increment = this.nestingLevel + 1;
            var message = increment == 1
                ? "+1"
                : $"+{increment} (incl {increment - 1} for nesting)";
            IncreaseComplexity(token, increment, message);
        }

        private void IncreaseComplexity(SyntaxToken token, int increment, string message)
        {
            Complexity += increment;
            this.incrementLocations.Add(new SecondaryLocation(token.GetLocation(), message));
        }
    }
}
