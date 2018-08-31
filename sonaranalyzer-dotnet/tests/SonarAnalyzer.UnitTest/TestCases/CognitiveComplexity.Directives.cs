using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SonarAnalyzer.UnitTest.TestCases
{
    class DirectivesCase
    {
#if A // Secondary {{+1}}
        public
#else // Secondary {{+1}}
        private
#endif
        void Foo() // Noncompliant {{Refactor this method to reduce its Cognitive Complexity from 3 to the 0 allowed.}}
        {
            if (true) // Secondary {{+1}}
            {
            }
        }

#if !A // Secondary {{+1}}
        public void Bar() // Noncompliant {{Refactor this method to reduce its Cognitive Complexity from 1 to the 0 allowed.}}
        {
        }
#endif

#if !A // Secondary {{+1}}
        public void FooBar() // Noncompliant {{Refactor this method to reduce its Cognitive Complexity from 9 to the 0 allowed.}}
        {
#if !B // Secondary {{+2 (incl 1 for nesting)}}
            if (true) // Secondary {{+3 (incl 2 for nesting)}}
            {
#endif
                var x = 1;
#if !C // Secondary {{+3 (incl 2 for nesting)}}
            }
#endif
        }
#endif
    }
}
