using System;
using System.Collections.Generic;

// 1. check resident behaviour tree from root each frame
// 2. selector uses first child that does not fail
// 3. sequence stops on first failure
// 4. condition returns success or failure, action can stay running
// 5. check from top each tick so higher priority actions can interrupt
namespace TVPatrol.AI.BehaviourTree
{
    public enum Status { Success, Failure, Running }

    public abstract class Node
    {
        public string Name { get; }
        protected Node(string name) => Name = name;
        public abstract Status Tick();
    }

    public sealed class Selector : Node
    {
        readonly List<Node> children;
        public Selector(string name, params Node[] children) : base(name) => this.children = new List<Node>(children);

        public override Status Tick()
        {
            foreach (var child in children)
            {
                var status = child.Tick();
                if (status != Status.Failure) return status;
            }
            return Status.Failure;
        }
    }

    public sealed class Sequence : Node
    {
        readonly List<Node> children;
        public Sequence(string name, params Node[] children) : base(name) => this.children = new List<Node>(children);

        public override Status Tick()
        {
            foreach (var child in children)
            {
                var status = child.Tick();
                if (status != Status.Success) return status;
            }
            return Status.Success;
        }
    }

    public sealed class Condition : Node
    {
        readonly Func<bool> check;
        public Condition(string name, Func<bool> check) : base(name) => this.check = check;
        public override Status Tick() => check() ? Status.Success : Status.Failure;
    }

    public sealed class ActionNode : Node
    {
        readonly Func<Status> action;
        public ActionNode(string name, Func<Status> action) : base(name) => this.action = action;
        public override Status Tick() => action();
    }
    // swap success and failure, keep running unchanged
    public sealed class Inverter : Node
    {
        readonly Node child;
        public Inverter(string name, Node child) : base(name) => this.child = child;
        public override Status Tick()
        {
            Status status = child.Tick();
            if (status == Status.Success) return Status.Failure;
            if (status == Status.Failure) return Status.Success;
            return Status.Running;
        }
    }

    // 1. wait for cooldown before successful action runs again
    // 2. use supplied clock for timing
    public sealed class Cooldown : Node
    {
        readonly Node child;
        readonly float duration;
        readonly Func<float> clock;
        float availableAt = float.NegativeInfinity;

        public Cooldown(string name, Node child, float duration, Func<float> clock) : base(name)
        {
            this.child = child;
            this.duration = duration;
            this.clock = clock;
        }

        public override Status Tick()
        {
            if (clock() < availableAt) return Status.Failure;
            Status result = child.Tick();
            if (result == Status.Success) availableAt = clock() + duration;
            return result;
        }
    }
}
