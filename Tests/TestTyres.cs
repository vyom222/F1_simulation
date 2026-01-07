// using Microsoft.VisualStudio.TestTools.UnitTesting;
// using F1_simulation.Core.Strategy_solver;
// using F1_simulation.External;

// namespace TyreTests
// {
//     [TestClass]
//     public sealed class Test1
//     {
//         [TestMethod]
//         public void TyreUsage_Bitmask_Correctly_Tracks_Compounds1()
//         {
//             OptimalStrategy.TyreUsage usage =
//             Optimal_strategy.TyreUsage.Soft |
//             Optimal_strategy.TyreUsage.Medium;

//             Assert.IsTrue(usage.HasFlag(Optimal_strategy.TyreUsage.Soft));
//             Assert.IsTrue(usage.HasFlag(Optimal_strategy.TyreUsage.Medium));
//             Assert.IsFalse(usage.HasFlag(Optimal_strategy.TyreUsage.Hard));
//         }

//         [TestMethod]
//         public void TyreUsage_Bitmask_Correctly_Tracks_Compounds2()
//         {
//             Optimal_strategy.TyreUsage usage =
//             Optimal_strategy.TyreUsage.Hard |
//             Optimal_strategy.TyreUsage.Soft;

//             Assert.IsTrue(usage.HasFlag(Optimal_strategy.TyreUsage.Soft));
//             Assert.IsFalse(usage.HasFlag(Optimal_strategy.TyreUsage.Medium));
//             Assert.IsTrue(usage.HasFlag(Optimal_strategy.TyreUsage.Hard));
//         }

//         [TestMethod]
//         public async Task TyreModelApi_Returns_All_Three_Compounds()
//         {
//             var results = await TyreModelClient.CallTyreModelAsync("Spain", 2024);

//             var compounds = results.Select(r => r.Compound).ToHashSet();

//             CollectionAssert.IsSubsetOf(
//                 new[] { "SOFT", "MEDIUM", "HARD" },
//                 compounds.ToList()
//             );
//         }


//     }
// }
