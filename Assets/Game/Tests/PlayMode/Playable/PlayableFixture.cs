using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CatMiners.Core.Sim;
using CatMiners.Playable;
using CatMiners.Runtime;
using CatMiners.Runtime.Outpost;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace CatMiners.Tests
{
    /// <summary>
    /// Сцена CoreGame в playable-режиме: <see cref="PlayableMode.Install"/> до загрузки, ManualClock, сохранения во временном
    /// каталоге, переход в магазин записывается в <see cref="m_StoreOpened"/> вместо открытия браузера. TearDown сначала снимает
    /// все крючки основной игры и статические поля (часы, каталог, конфиги, окно офлайна, резерв под баннер, аспект камеры,
    /// открытие магазина, шаг кадра) — даже если выгрузка сцены упадёт, playable не остаётся включённым для других тестов, —
    /// затем выгружает сцену. Ожидания — по кадрам с фиксированным шагом анимаций (<see cref="FrameStep"/>) и по ManualClock,
    /// без реального времени.
    /// </summary>
    public abstract class PlayableFixture
    {
        protected ManualClock m_Clock;
        protected string m_Dir;
        protected OutpostController Game => GameBootstrap.OutpostController;
        protected PlayableDirector Director => Game.GetComponent<PlayableDirector>();
        protected PlayableQuests Quests => Game.GetComponent<PlayableQuests>();
        protected PlayableJuice Juice => Game.GetComponent<PlayableJuice>();
        protected PlayableCta Cta => Game.GetComponent<PlayableCta>();
        /// <summary>Ссылки, которые открыл бы <see cref="PlayableStore.Open"/>.</summary>
        protected readonly List<string> m_StoreOpened = new List<string>();
        /// <summary>Аспект камеры (0 — окно редактора); снимки — 9:16.</summary>
        protected virtual float Aspect => 0f;
        /// <summary>Шаг кадра анимаций по FrameTime (замах кирки, въезд панелей) в ожиданиях: не зависит от загрузки PC.</summary>
        protected const float FrameStep = 1f / 30f;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            m_Dir = Path.Combine(Application.temporaryCachePath, "PlayableTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(m_Dir);
            m_Clock = new ManualClock(1_800_000_000_000L);
            CameraRig.OverrideAspect = Aspect;
            OutpostController.ClockOverride = m_Clock;
            OutpostController.SaveDirectoryOverride = m_Dir;
            OutpostController.FileSystemOverride = null;
            GameBootstrap.LegacyMode = false;
            m_StoreOpened.Clear();
            PlayableStore.OpenOverride = m_StoreOpened.Add;
            PlayableMode.Install();
            yield return SceneManager.LoadSceneAsync("CoreGame");
            yield return null;
            Assert.That(Game, Is.Not.Null);
            Assert.That(Game.Phase, Is.EqualTo(OutpostControllerPhase.Playing));
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            // Крючки и статические поля — до выгрузки: сцена, загруженная после этого теста, уже без playable.
            ResetStatics();
            var empty = SceneManager.CreateScene("PlayableCleanup-" + Guid.NewGuid().ToString("N"));
            SceneManager.SetActiveScene(empty);
            var loaded = SceneManager.GetSceneByName("CoreGame");
            if (loaded.IsValid()) yield return SceneManager.UnloadSceneAsync(loaded);
            try { Directory.Delete(m_Dir, true); } catch (Exception) { }
        }

        static void ResetStatics()
        {
            Time.captureDeltaTime = 0f;
            PlayableMode.Uninstall();
            OutpostController.ClockOverride = null;
            OutpostController.SaveDirectoryOverride = null;
            OutpostController.FileSystemOverride = null;
            OutpostController.ConfigsOverride = null;
            OutpostController.SuppressOfflineSummary = false;
            OutpostHud.BottomReserve = 0f;
            CameraRig.OverrideAspect = 0f;
            PlayableStore.OpenOverride = null;
            GameBootstrap.LegacyMode = false;
            AudioListener.pause = false;
        }

        /// <summary>Часы вперёд шагами не больше секунды (иначе контроллер уходит в офлайн-расчёт), кадр на шаг.</summary>
        protected IEnumerator Advance(double seconds, double step = 0.25)
        {
            for (double t = 0; t < seconds - 1e-9; t += step)
            {
                m_Clock.Advance(step);
                yield return null;
            }
        }

        /// <summary>
        /// Кадры с фиксированным шагом анимаций <see cref="FrameStep"/>, пока <paramref name="done"/> не станет true (не больше
        /// <paramref name="frames"/>); часы — на <paramref name="clockStep"/> за кадр (подсказка пересчитывает цель по часам).
        /// </summary>
        protected IEnumerator WaitFrames(Func<bool> done, int frames = 240, double clockStep = 0.02)
        {
            float previous = Time.captureDeltaTime;
            Time.captureDeltaTime = FrameStep;
            try
            {
                for (int i = 0; i < frames && !done(); i++)
                {
                    if (clockStep > 0) m_Clock.Advance(clockStep);
                    yield return null;
                }
            }
            finally { Time.captureDeltaTime = previous; }
        }

        protected IEnumerator WaitPlaying()
        {
            for (int frame = 0; frame < 2000 && (Game.Phase != OutpostControllerPhase.Playing || Game.InTransition); frame++)
            {
                m_Clock.Advance(0.02);
                yield return null;
            }
            Assert.That(Game.Phase, Is.EqualTo(OutpostControllerPhase.Playing));
        }

        /// <summary>
        /// Удар киркой игрока по живому камню с наибольшим запасом HP (как тап): пауза между ударами — по часам, анимация замаха
        /// — кадрами с шагом <see cref="FrameStep"/> (часы стоят); затем кадр показа событий.
        /// </summary>
        protected IEnumerator StrikeOnce()
        {
            yield return Advance(0.5);
            var sim = Game.Sim;
            var target = Game.Map.Targets.Where(t => !t.IsGate && Game.CanStrike(t.Id))
                .OrderByDescending(t => sim.TargetById(t.Id).RemainingHpUnits).FirstOrDefault();
            Assert.That(target, Is.Not.Null, "есть камень под удар");
            Assert.That(Game.TryGetTargetWorldPosition(target.Id, out var world), Is.True);
            Assert.That(Game.BeginStrike(target.Id, world), Is.True);
            yield return WaitFrames(() => Game.Board.ActiveStrikes == 0, 300, 0);
            Assert.That(Game.Board.ActiveStrikes, Is.Zero, "замах закончен");
            yield return null;
            yield return null;
        }

        /// <summary>Золото на покупку командой BonusGold (как награда квеста), без окна.</summary>
        protected void GrantGold(System.Numerics.BigInteger amount)
        {
            if (amount.Sign > 0) Assert.That(Game.GrantRewardGold(amount).Ok, Is.True);
        }
    }
}
