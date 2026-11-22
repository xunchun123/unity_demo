using StarVortex.Core;
using StarVortex.Enemies;
using UnityEngine;

namespace StarVortex.Gameplay
{
    /// <summary>
    /// 爽点效果实现：勇者控制器在战斗节点调用这些钩子，爽点在这里改变行为。
    /// 面试点：爽点不是"数值 +N"，而是"攻击形态改变"——分裂/剑气/落雷/冻结，
    /// 每个都能在画面上一眼看出差别，这是"爽点"与"天赋"分轨的原因。
    /// </summary>
    public static class BoonEffects
    {
        private static int EnemyMask = -1;
        private static int Enemies
        {
            get
            {
                if (EnemyMask < 0) EnemyMask = 1 << LayerMask.NameToLayer("Enemy");
                return EnemyMask;
            }
        }

        /// <summary>冲刺时（火焰冲刺：在起点留一片燃烧地面）。</summary>
        public static void OnDash(HeroController hero)
        {
            if (!hero.Boons.Has(BoonIds.UniversalFlameDash)) return;
            BurningZone.Spawn(hero.transform.position + (Vector3)Vector2.down * 0.5f, 2.2f, 2.5f, hero.Stats.Attack * 0.5f);
        }

        /// <summary>近战命中（嗜血：回 2% 最大生命）。</summary>
        public static void OnMeleeHit(HeroController hero, EnemyBase enemy)
        {
            if (hero.Boons.Has(BoonIds.SwordLifesteal))
                hero.Heal(hero.Stats.MaxHp * 0.02f);
        }

        /// <summary>三段连击收尾（连击落雷：雷劈附近最多 3 个敌人；连击灼烧：收尾位置留火）。</summary>
        public static void OnComboFinisher(HeroController hero, Vector2 pos)
        {
            if (hero.Boons.Has(BoonIds.SwordComboBurn))
                BurningZone.Spawn(pos, 1.6f, 1.8f, hero.Stats.Attack * 0.5f);

            if (!hero.Boons.Has(BoonIds.SwordComboLightning)) return;
            int struck = 0;
            foreach (var hit in Physics2D.OverlapCircleAll(pos, 6f, Enemies))
            {
                if (struck >= 3) break;
                var enemy = hit.GetComponentInParent<EnemyBase>();
                if (enemy == null || enemy.IsDead) continue;
                Vector3 strikePos = enemy.transform.position + Vector3.up * 2.5f;
                enemy.TakeDamage(new DamageInfo(hero.Stats.Attack * 1.2f, strikePos, false));
                Juice.DeathBurst(strikePos, new Color(0.7f, 0.9f, 1.6f), 10, 0.3f);
                AudioService.Instance?.PlayWithPitch(SfxId.CritHit, 1.3f);
                struck++;
            }
        }

        /// <summary>空中下劈（震地斩：落地冲击波范围伤害+击退）。</summary>
        public static void OnQuakeSlam(HeroController hero)
        {
            Vector2 center = hero.transform.position;
            foreach (var hit in Physics2D.OverlapCircleAll(center, 4.5f, Enemies))
            {
                var enemy = hit.GetComponentInParent<EnemyBase>();
                if (enemy == null || enemy.IsDead) continue;
                enemy.TakeDamage(new DamageInfo(hero.Stats.Attack * 1.5f, center, false));
                enemy.ApplyKnockback(((Vector2)enemy.transform.position - center).normalized, 8f);
            }
            CameraRig.Instance?.AddTrauma(0.35f);
            Juice.DeathBurst((Vector3)(center + Vector2.down * 0.5f), new Color(1.4f, 1.1f, 0.5f), 22, 0.8f);
            AudioService.Instance?.Play(SfxId.EliteKill);
        }

        /// <summary>法杖新星（冰霜新星爽点会冻结周围敌人 1.5 秒）。</summary>
        public static void OnStaffNova(HeroController hero)
        {
            bool frost = hero.Boons.Has(BoonIds.StaffFrostNova);
            Vector2 center = hero.transform.position;
            float radius = 5f;
            foreach (var hit in Physics2D.OverlapCircleAll(center, radius, Enemies))
            {
                var enemy = hit.GetComponentInParent<EnemyBase>();
                if (enemy == null || enemy.IsDead) continue;
                enemy.TakeDamage(new DamageInfo(hero.Stats.Attack * 1.8f, center, false));
                enemy.ApplyKnockback(((Vector2)enemy.transform.position - center).normalized, 6f);
                if (frost) enemy.SetFrozen(1.5f);
            }
            Juice.LevelUpRing(center); // 复用升级光环粒子做新星视觉
            Juice.DeathBurst(center, frost ? new Color(0.6f, 1.2f, 1.8f) : new Color(1.2f, 0.7f, 1.8f), 18, radius * 0.5f);
        }

        /// <summary>箭雨（K 键：面前区域天降 8 箭）。</summary>
        public static void OnArrowRain(HeroController hero)
        {
            var prefab = ProjectilePool.Get(ProjectilePool.Arrow);
            if (prefab == null) return;
            float baseX = hero.transform.position.x + (hero.FacingRight ? 2f : -2f);
            for (int i = 0; i < 8; i++)
            {
                var go = PoolService.Instance.Spawn(prefab,
                    new Vector3(baseX + Random.Range(-3f, 3f), hero.transform.position.y + 7f, 0f),
                    Quaternion.identity);
                go.GetComponent<Projectile>().Launch(Vector2.down, 14f, hero.Stats.Attack * 0.9f, false, 2, 2.5f);
            }
            AudioService.Instance?.PlayWithPitch(SfxId.Shoot, 0.9f);
        }
    }
}
