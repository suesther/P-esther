using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using Plang.Compiler.TypeChecker;
using Plang.Compiler.TypeChecker.AST;
using Plang.Compiler.TypeChecker.AST.Declarations;
using Plang.Compiler.TypeChecker.AST.Statements;
using Plang.Compiler.TypeChecker.AST.States;

namespace Plang.Compiler.Backend.Stately {
    public class StatelyCodeGenerator : ICodeGenerator
    {
        public bool HasCompilationStage => false;
        public IEnumerable<CompiledFile> GenerateCode(ICompilerConfiguration job, Scope globalScope)
        {
            var context = new CompilationContext(job);
            var statelySource = GenerateSource(context, globalScope);
            return new List<CompiledFile> { statelySource };
        }

        private CompiledFile GenerateSource(CompilationContext context, Scope globalScope)
        {
            var source = new CompiledFile(context.FileName);
            WriteSourcePrologue(context, source.Stream);
            // write the top level declarations
            foreach (var decl in globalScope.AllDecls)
            {
                WriteDecl(context, source.Stream, decl);
            }

            return source;
        }

        private void WriteDecl(CompilationContext context, StringWriter output, IPDecl decl)
        {
            //string declName;
            switch (decl)
            {
                case Machine machine:
                    if (!machine.IsSpec)
                    {
                        WriteMachine(context, output, machine);
                    }

                    break;
            }
        }

        //Collects all the go-to's within a function call, iteratively 
        private List<String> WriteStmt(IPStmt statement)
        {
            var gotoStmts = new List<String>();
            switch (statement)
            {
                case GotoStmt goStmt:
                    gotoStmts.Add(goStmt.State.Name);
                    break;
                case FunCallStmt fStmt:
                    foreach (var stmt in fStmt.Function.Body.Statements)
                    {
                        gotoStmts = gotoStmts.Union(WriteStmt(stmt)).ToList();
                    }
                    break;
                case IfStmt ifStmt:
                    foreach (var stmt in ifStmt.ThenBranch.Statements)
                    {
                        gotoStmts = gotoStmts.Union(WriteStmt(stmt)).ToList();
                    }
                    break;
            }

            return gotoStmts;
        }
        
        //Handles writing all instances of a machine
        private void WriteMachine(CompilationContext context, StringWriter output, Machine machine)
        {

            context.WriteLine(output, $"const {machine.Name} = createMachine<Context>({{");
            context.WriteLine(output, $"id: \"{machine.Name}\",");
            
            //Code start state of machine.
            context.WriteLine(output,$"initial: \"{machine.StartState.Name}\", ");
            
            //Code up the states in each machine.
            context.WriteLine(output, "states: {");
            foreach (State state in machine.States)
            {
                
                context.WriteLine(output,$"{state.Name}: {{");
                WriteState(context, output, state);
                context.WriteLine(output, state.Equals(machine.States.Last()) ? "}" : "},");
            }
            context.WriteLine(output, "}");
            context.WriteLine(output, "});");
        }
        
        //Handles writing all instances of states (within a machine)
        private void WriteState(CompilationContext context, StringWriter output, State state)
        {
            //Entry function exists!
            if (state.Entry != null) {
                foreach (var s in state.Entry.Body.Statements) {
                    if (s.GetType() == typeof(GotoStmt)) {
                        var x = (GotoStmt)s;
                        context.WriteLine(output, "always: [");
                        context.WriteLine(output, $"{{target: '{x.State.Name}'}}");
                        context.WriteLine(output, "]");
                    }
                }
            }
            //All the go to Statements in a state (of a machine)
            var gotoStmts = new Dictionary<String, List<String>>();
            foreach (var pair in state.AllEventHandlers)
            {
                var handledEvent = pair.Key;
                
                //context.WriteLine(output, $"{pair.Value}");
                switch (pair.Value)
                {
                    //on... goto...
                    case EventGotoState goAct:
                        List<String> target = new List<String> { goAct.Target.Name };
                        if (gotoStmts.ContainsKey(goAct.Trigger.Name))
                        {
                            target.AddRange(gotoStmts[goAct.Trigger.Name]);
                        }
                        gotoStmts[goAct.Trigger.Name] =  target;
                        break;
                    //on... do...
                    case EventDoAction doAct:
                        foreach (var stmt in doAct.Target.Body.Statements)
                        {
                            List<String> funCallS = WriteStmt(stmt);
                            if (gotoStmts.ContainsKey(doAct.Trigger.Name))
                            {
                                funCallS.AddRange(gotoStmts[doAct.Trigger.Name]);
                            }
                            gotoStmts[doAct.Trigger.Name] =  funCallS;
                        }
                        break;
                    
                }
            }
            
            //Writes out all the Go-To Statements collected within a state into the code.
            if (gotoStmts.Any())
            {
                context.WriteLine(output, "on: {");
                foreach (var stmt in gotoStmts)
                {
                    context.WriteLine(output, $"{stmt.Key} : {{ target: [");
                    foreach (var target in stmt.Value)
                    {
                        context.WriteLine(output, $"\"{target}\",");
                    }
                    context.WriteLine(output, "]");
                    context.WriteLine(output, "},");
                }
                context.WriteLine(output, "}");
            }

        }
        private void WriteSourcePrologue(CompilationContext context, StringWriter output)
        {
            context.WriteLine(output, "import { createMachine, assign } from 'xstate';");
            context.WriteLine(output, "interface Context {retries: number;}");
        }
    }
}