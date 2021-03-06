﻿using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.Server.Atmos;
using Content.Server.GameObjects.Components.Body.Behavior;
using Content.Server.GameObjects.Components.Body.Circulatory;
using Content.Server.GameObjects.Components.Metabolism;
using Content.Shared.Atmos;
using Content.Shared.GameObjects.Components.Body.Mechanism;
using NUnit.Framework;
using Robust.Server.Interfaces.Maps;
using Robust.Shared.Interfaces.GameObjects;
using Robust.Shared.Interfaces.Map;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.Body
{
    [TestFixture]
    [TestOf(typeof(LungBehaviorComponent))]
    public class LungTest : ContentIntegrationTest
    {
        [Test]
        public async Task AirConsistencyTest()
        {
            var server = StartServerDummyTicker();

            server.Assert(() =>
            {
                var mapManager = IoCManager.Resolve<IMapManager>();

                mapManager.CreateNewMapEntity(MapId.Nullspace);

                var entityManager = IoCManager.Resolve<IEntityManager>();

                var human = entityManager.SpawnEntity("HumanMob_Content", MapCoordinates.Nullspace);

                Assert.True(human.TryGetMechanismBehaviors(out List<LungBehaviorComponent> lungs));
                Assert.That(lungs.Count, Is.EqualTo(1));
                Assert.True(human.TryGetComponent(out BloodstreamComponent bloodstream));

                var gas = new GasMixture(1);

                var originalOxygen = 2;
                var originalNitrogen = 8;
                var breathedPercentage = Atmospherics.BreathPercentage;

                gas.AdjustMoles(Gas.Oxygen, originalOxygen);
                gas.AdjustMoles(Gas.Nitrogen, originalNitrogen);

                var lung = lungs[0];
                lung.Inhale(1, gas);

                var lungOxygen = originalOxygen * breathedPercentage;
                var lungNitrogen = originalNitrogen * breathedPercentage;

                Assert.That(bloodstream.Air.GetMoles(Gas.Oxygen), Is.EqualTo(lungOxygen));
                Assert.That(bloodstream.Air.GetMoles(Gas.Nitrogen), Is.EqualTo(lungNitrogen));

                var mixtureOxygen = originalOxygen - lungOxygen;
                var mixtureNitrogen = originalNitrogen - lungNitrogen;

                Assert.That(gas.GetMoles(Gas.Oxygen), Is.EqualTo(mixtureOxygen));
                Assert.That(gas.GetMoles(Gas.Nitrogen), Is.EqualTo(mixtureNitrogen));

                var lungOxygenBeforeExhale = lung.Air.GetMoles(Gas.Oxygen);
                var lungNitrogenBeforeExhale = lung.Air.GetMoles(Gas.Nitrogen);

                // Empty after it transfer to the bloodstream
                Assert.Zero(lungOxygenBeforeExhale);
                Assert.Zero(lungNitrogenBeforeExhale);

                lung.Exhale(1, gas);

                var lungOxygenAfterExhale = lung.Air.GetMoles(Gas.Oxygen);
                var exhaledOxygen = lungOxygenBeforeExhale - lungOxygenAfterExhale;

                // Not completely empty
                Assert.Positive(lung.Air.Gases.Sum());

                // Retains needed gas
                Assert.Positive(bloodstream.Air.GetMoles(Gas.Oxygen));

                // Expels toxins
                Assert.Zero(bloodstream.Air.GetMoles(Gas.Nitrogen));

                mixtureOxygen += exhaledOxygen;

                var finalTotalOxygen = gas.GetMoles(Gas.Oxygen) +
                                         bloodstream.Air.GetMoles(Gas.Oxygen) +
                                         lung.Air.GetMoles(Gas.Oxygen);

                // No ticks were run, metabolism doesn't run and so no oxygen is used up
                Assert.That(finalTotalOxygen, Is.EqualTo(originalOxygen));
                Assert.That(gas.GetMoles(Gas.Oxygen), Is.EqualTo(mixtureOxygen).Within(0.000001f));

                var finalTotalNitrogen = gas.GetMoles(Gas.Nitrogen) +
                                         bloodstream.Air.GetMoles(Gas.Nitrogen) +
                                         lung.Air.GetMoles(Gas.Nitrogen);

                // Nitrogen stays constant
                Assert.That(finalTotalNitrogen, Is.EqualTo(originalNitrogen).Within(0.000001f));
            });

            await server.WaitIdleAsync();
        }

        [Test]
        public async Task NoSuffocationTest()
        {
            var server = StartServerDummyTicker();
            await server.WaitIdleAsync();

            var mapLoader = server.ResolveDependency<IMapLoader>();
            var mapManager = server.ResolveDependency<IMapManager>();
            var entityManager = server.ResolveDependency<IEntityManager>();

            MapId mapId;
            IMapGrid grid = null;
            MetabolismComponent metabolism = null;
            IEntity human = null;

            var testMapName = "Maps/Test/Breathing/3by3-20oxy-80nit.yml";

            await server.WaitPost(() =>
            {
                mapId = mapManager.CreateMap();
                grid = mapLoader.LoadBlueprint(mapId, testMapName);
            });

            Assert.NotNull(grid, $"Test blueprint {testMapName} not found.");

            await server.WaitAssertion(() =>
            {
                var center = new Vector2(0.5f, -1.5f);
                var coordinates = new EntityCoordinates(grid.GridEntityId, center);
                human = entityManager.SpawnEntity("HumanMob_Content", coordinates);

                Assert.True(human.HasMechanismBehavior<LungBehaviorComponent>());
                Assert.True(human.TryGetComponent(out metabolism));
                Assert.False(metabolism.Suffocating);
            });

            var increment = 10;

            for (var tick = 0; tick < 600; tick += increment)
            {
                await server.WaitRunTicks(increment);
                Assert.False(metabolism.Suffocating, $"Entity {human.Name} is suffocating on tick {tick}");
            }

            await server.WaitIdleAsync();
        }
    }
}
