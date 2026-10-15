using Xunit;

// The leak tests observe SafeArenaHandle.LiveCount, a process-global counter,
// so two tests of this assembly must never overlap. The [Collection("Arena")]
// attributes group the arena tests explicitly; this attribute removes the
// remaining parallelization between collections, so a new test class cannot
// break the counter by accident.
[assembly: CollectionBehavior(DisableTestParallelization = true)]