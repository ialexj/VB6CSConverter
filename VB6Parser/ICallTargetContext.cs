using Antlr4.Runtime.Tree;
using static VB6Parser.VisualBasic6Parser;

namespace VB6Parser
{
    public interface ICallContext : IRuleNode, IParseTree, ISyntaxTree, ITree
    {
        bool IsPartial { get; }

        IEnumerable<ICallTargetContext> segments();

        DictionaryCallStmtContext dictionaryCallStmt();
    }

    public interface ICallTargetContext : IRuleNode, IParseTree, ISyntaxTree, ITree
    {
        IIdentifierContext identifier();

        TypeHintContext typeHint();

        ArgsCallContext[] args();

        DictionaryCallStmtContext dictionaryCallStmt();
    }
    
    public partial class VisualBasic6Parser
    {
        public partial class BlockStmtContext
        {
            public ICallContext call() => (
                implicitCallStmt_InBlock() as ICallContext ??
                explicitCallStmt() as ICallContext
            );
        }

        public partial class ImplicitCallStmt_InBlockContext : ICallContext
        {
            public bool IsPartial => iCS_B_MemberProcedureCall()?.IsPartial ?? false;

            public DictionaryCallStmtContext dictionaryCallStmt() => null;

            public IEnumerable<ICallTargetContext> segments()
            {
                if (iCS_B_ProcedureCall() is ICS_B_ProcedureCallContext proc) {
                    yield return proc;
                }
                else if (iCS_B_MemberProcedureCall() is ICS_B_MemberProcedureCallContext member) {
                    foreach (var m in member.segments()) {
                        yield return m;
                    }
                }
            }
        }

        public partial class ExplicitCallStmtContext : ICallContext
        {
            public bool IsPartial => eCS_MemberProcedureCall()?.IsPartial ?? false;

            public DictionaryCallStmtContext dictionaryCallStmt() => eCS_MemberProcedureCall()?.dictionaryCallStmt();

            public IEnumerable<ICallTargetContext> segments()
            {
                if (eCS_ProcedureCall() is ECS_ProcedureCallContext proc) {
                    yield return proc;
                }
                else if (eCS_MemberProcedureCall() is ECS_MemberProcedureCallContext member) {
                    foreach (var m in member.segments()) {
                        yield return m;
                    }
                }
            }
        }


        public partial class ICS_B_ProcedureCallContext : ICallTargetContext
        {
            public IIdentifierContext identifier() => certainIdentifier();

            public TypeHintContext typeHint() => null;

            public DictionaryCallStmtContext dictionaryCallStmt() => null;

            public ArgsCallContext[] args() => argsCall() is ArgsCallContext a ? [a] : [];
        }

        public partial class ECS_ProcedureCallContext : ICallTargetContext
        {
            public IIdentifierContext identifier() => ambiguousIdentifier();

            public DictionaryCallStmtContext dictionaryCallStmt() => null;

            public ArgsCallContext[] args() => argsCall() is ArgsCallContext a ? [a] : [];
        }


        public partial class ICS_B_MemberProcedureCallContext : ICallContext, ICallTargetContext
        {
            public bool IsPartial => implicitCallStmt_InStmt() is null && DOT() is not null;

            public IIdentifierContext identifier() => ambiguousIdentifier();

            public ArgsCallContext[] args() => argsCall() is ArgsCallContext a ? [a] : [];

            public IEnumerable<ICallTargetContext> segments() 
            {
                if (implicitCallStmt_InStmt() is ImplicitCallStmt_InStmtContext stmt) {
                    foreach (var m in stmt.segments()) {
                        yield return m;
                    }
                }

                yield return this;
            }
        }

        public partial class ECS_MemberProcedureCallContext : ICallContext, ICallTargetContext
        {
            public bool IsPartial => implicitCallStmt_InStmt() is null && DOT() is not null;

            public DictionaryCallStmtContext dictionaryCallStmt() => null;

            public IIdentifierContext identifier() => ambiguousIdentifier();

            public ArgsCallContext[] args() => argsCall() is ArgsCallContext a ? [a] : [];

            public IEnumerable<ICallTargetContext> segments()
            {
                if (implicitCallStmt_InStmt() is ImplicitCallStmt_InStmtContext stmt) {
                    foreach (var m in stmt.segments()) {
                        yield return m;
                    }
                }

                yield return this;
            }
        }



        // Implict call in statement - call expressions

        public partial class ImplicitCallStmt_InStmtContext : ICallContext
        {
            public bool IsPartial
            {
                get {
                    if (iCS_S_MembersCall()?.IsPartial ?? false) {
                        return true;
                    }

                    // Dictionary only
                    if (iCS_S_VariableOrProcedureCall() is null 
                        && iCS_S_ProcedureOrArrayCall() is null 
                        && iCS_S_MembersCall() is null) {
                        return true;
                    }

                    return false;
                }
            }

            public DictionaryCallStmtContext dictionaryCallStmt() 
                => iCS_S_DictionaryCall()?.dictionaryCallStmt() 
                ?? iCS_S_ProcedureOrArrayCall()?.dictionaryCallStmt() 
                ?? iCS_S_VariableOrProcedureCall()?.dictionaryCallStmt()
                ?? iCS_S_MembersCall()?.dictionaryCallStmt()
                ?? iCS_S_MembersCall()?.iCS_S_MemberCall()
                    .LastOrDefault().target().dictionaryCallStmt();

            public IEnumerable<ICallTargetContext> segments()
            {
                if (iCS_S_MembersCall() is ICS_S_MembersCallContext members) {
                    foreach (var member in members.segments()) {
                        yield return member;
                    }
                }

                if (iCS_S_VariableOrProcedureCall() is ICS_S_VariableOrProcedureCallContext vpc) {
                    yield return vpc;
                }
                else if (iCS_S_ProcedureOrArrayCall() is ICS_S_ProcedureOrArrayCallContext proc) {
                    yield return proc;
                }
            }
        }

        public partial class ICS_S_VariableOrProcedureCallContext : ICallTargetContext
        {
            public IIdentifierContext identifier() => ambiguousIdentifier();

            public ArgsCallContext[] args() => [];
        }

        public partial class ICS_S_ProcedureOrArrayCallContext : ICallTargetContext
        {
            public IIdentifierContext identifier() => ambiguousIdentifier();

            public ArgsCallContext[] args() => argsCall();
        }

        public partial class ICS_S_MembersCallContext : ICallContext
        {
            public bool IsPartial => iCS_S_VariableOrProcedureCall() is null && iCS_S_ProcedureOrArrayCall() is null;

            DictionaryCallStmtContext ICallContext.dictionaryCallStmt() 
                => dictionaryCallStmt() 
                ?? iCS_S_MemberCall().LastOrDefault().target().dictionaryCallStmt();

            public IEnumerable<ICallTargetContext> segments()
            {
                if (iCS_S_VariableOrProcedureCall() is ICS_S_VariableOrProcedureCallContext vpc) {
                    yield return vpc;
                }
                else if (iCS_S_ProcedureOrArrayCall() is ICS_S_ProcedureOrArrayCallContext procarr) {
                    yield return procarr;
                }

                foreach (var member in iCS_S_MemberCall()) {
                    yield return member.target();
                }
            }
        }

        public partial class ICS_S_MemberCallContext
        {
            public ICallTargetContext target() =>
                iCS_S_VariableOrProcedureCall() as ICallTargetContext ??
                iCS_S_ProcedureOrArrayCall() as ICallTargetContext;
        }

        public partial class ICS_S_DictionaryCallContext { }


        public partial class ICS_S_NestedProcedureCallContext : ICallTargetContext
        {
            public ArgsCallContext[] args() => argsCall() is ArgsCallContext a ? [a] : [];

            public DictionaryCallStmtContext dictionaryCallStmt() => null;

            public IIdentifierContext identifier() => ambiguousIdentifier();
        }
    }
}
