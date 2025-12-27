using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Kingmaker;
using Kingmaker.AI;
using Kingmaker.AI.AreaScanning;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.Pathfinding;
using Kingmaker.RuleSystem;
using Kingmaker.RuleSystem.Rules;
using Kingmaker.UnitLogic.Commands;
using Kingmaker.Utility;
using Kingmaker.View.Covers;
using Pathfinding;
using UnityEngine;
using CompanionAI_v2_2.Settings;

namespace CompanionAI_v2_2.Core
{
    /// <summary>
    /// ★ v2.2.38: 통합 이동 API
    /// 게임의 PathfindingService, LosCalculations 등을 래핑하여
    /// AI 이동 결정을 위한 통합 인터페이스 제공
    /// </summary>
    public static class MovementAPI
    {
        #region Position Scoring

        /// <summary>
        /// 위치 평가 결과
        /// </summary>
        public class PositionScore
        {
            public CustomGridNodeBase Node { get; set; }
            public Vector3 Position => Node?.Vector3Position ?? Vector3.zero;

            // 점수 구성요소
            public float CoverScore { get; set; }        // 엄폐물 점수 (높을수록 좋음)
            public float DistanceScore { get; set; }     // 거리 점수 (목표에 따라 다름)
            public float ThreatScore { get; set; }       // 위협 점수 (낮을수록 좋음)
            public float AttackScore { get; set; }       // 공격 가능성 점수
            public float APCost { get; set; }            // 이동 AP 비용

            public float TotalScore => CoverScore + DistanceScore - ThreatScore + AttackScore;

            // 메타데이터
            public bool CanStand { get; set; }
            public bool HasLosToEnemy { get; set; }
            public int ProvokedAttacks { get; set; }
            public LosCalculations.CoverType BestCover { get; set; }

            public override string ToString() =>
                $"Pos({Position.x:F1},{Position.z:F1}) Score={TotalScore:F1} (Cover={CoverScore:F1}, Dist={DistanceScore:F1}, Threat={ThreatScore:F1}, Attack={AttackScore:F1})";
        }

        /// <summary>
        /// 이동 목적 유형
        /// </summary>
        public enum MovementGoal
        {
            FindCover,          // 엄폐물 찾기 (원거리 캐릭터)
            MaintainDistance,   // 거리 유지 (최소 거리 유지)
            ApproachEnemy,      // 적 접근 (근접 캐릭터)
            AttackPosition,     // 공격 가능 위치 (사거리 내)
            Retreat,            // 후퇴 (적에게서 멀어지기)
            RangedAttackPosition // ★ v2.2.43: 원거리 공격 위치 (안전거리 + 사거리 내)
        }

        #endregion

        #region Tile Discovery

        /// <summary>
        /// 유닛이 도달 가능한 모든 타일 찾기
        /// </summary>
        public static async Task<Dictionary<GraphNode, WarhammerPathAiCell>> FindAllReachableTiles(
            BaseUnitEntity unit,
            float? maxAP = null)
        {
            if (unit == null) return new Dictionary<GraphNode, WarhammerPathAiCell>();

            try
            {
                float ap = maxAP ?? unit.CombatState?.ActionPointsBlue ?? 0f;
                if (ap <= 0) return new Dictionary<GraphNode, WarhammerPathAiCell>();

                var pathData = await AiAreaScanner.FindAllReachableNodesAsync(unit, unit.Position, ap);

                if (pathData.IsZero)
                {
                    Main.LogDebug($"[MovementAPI] {unit.CharacterName}: No reachable tiles");
                    return new Dictionary<GraphNode, WarhammerPathAiCell>();
                }

                Main.LogDebug($"[MovementAPI] {unit.CharacterName}: Found {pathData.cells.Count} reachable tiles");
                return pathData.cells;
            }
            catch (Exception ex)
            {
                Main.LogDebug($"[MovementAPI] FindAllReachableTiles error: {ex.Message}");
                return new Dictionary<GraphNode, WarhammerPathAiCell>();
            }
        }

        /// <summary>
        /// 동기 버전 - 도달 가능한 타일 찾기
        /// </summary>
        public static Dictionary<GraphNode, WarhammerPathPlayerCell> FindAllReachableTilesSync(
            BaseUnitEntity unit,
            float? maxAP = null)
        {
            if (unit == null) return new Dictionary<GraphNode, WarhammerPathPlayerCell>();

            try
            {
                float ap = maxAP ?? unit.CombatState?.ActionPointsBlue ?? 0f;
                if (ap <= 0) return new Dictionary<GraphNode, WarhammerPathPlayerCell>();

                var agent = unit.View?.MovementAgent;
                if (agent == null) return new Dictionary<GraphNode, WarhammerPathPlayerCell>();

                var tiles = PathfindingService.Instance.FindAllReachableTiles_Blocking(
                    agent,
                    unit.Position,
                    ap,
                    ignoreThreateningAreaCost: false
                );

                Main.LogDebug($"[MovementAPI] {unit.CharacterName}: Found {tiles?.Count ?? 0} reachable tiles (sync)");
                return tiles ?? new Dictionary<GraphNode, WarhammerPathPlayerCell>();
            }
            catch (Exception ex)
            {
                Main.LogDebug($"[MovementAPI] FindAllReachableTilesSync error: {ex.Message}");
                return new Dictionary<GraphNode, WarhammerPathPlayerCell>();
            }
        }

        #endregion

        #region Position Evaluation

        /// <summary>
        /// 모든 도달 가능 위치 평가
        /// </summary>
        public static List<PositionScore> EvaluateAllPositions(
            BaseUnitEntity unit,
            Dictionary<GraphNode, WarhammerPathAiCell> reachableTiles,
            List<BaseUnitEntity> enemies,
            MovementGoal goal,
            float targetDistance = 10f)
        {
            var scores = new List<PositionScore>();
            if (unit == null || reachableTiles == null || reachableTiles.Count == 0)
                return scores;

            foreach (var kvp in reachableTiles)
            {
                var node = kvp.Key as CustomGridNodeBase;
                var cell = kvp.Value;

                if (node == null || !cell.IsCanStand)
                    continue;

                var score = EvaluatePosition(unit, node, cell, enemies, goal, targetDistance);
                scores.Add(score);
            }

            return scores.OrderByDescending(s => s.TotalScore).ToList();
        }

        /// <summary>
        /// 단일 위치 평가
        /// </summary>
        public static PositionScore EvaluatePosition(
            BaseUnitEntity unit,
            CustomGridNodeBase node,
            WarhammerPathAiCell cell,
            List<BaseUnitEntity> enemies,
            MovementGoal goal,
            float targetDistance = 10f)
        {
            var score = new PositionScore
            {
                Node = node,
                CanStand = cell.IsCanStand,
                APCost = cell.Length,
                ProvokedAttacks = cell.ProvokedAttacks,
                BestCover = LosCalculations.CoverType.None
            };

            if (enemies == null || enemies.Count == 0)
                return score;

            // 각 적에 대해 평가
            float totalCoverScore = 0f;
            float nearestEnemyDist = float.MaxValue;
            bool hasAnyLos = false;

            foreach (var enemy in enemies)
            {
                if (enemy == null || enemy.LifeState.IsDead) continue;

                var enemyNode = enemy.Position.GetNearestNodeXZ();
                if (enemyNode == null) continue;

                float dist = Vector3.Distance(node.Vector3Position, enemy.Position);
                if (dist < nearestEnemyDist) nearestEnemyDist = dist;

                // LOS 및 엄폐물 체크
                try
                {
                    var los = LosCalculations.GetWarhammerLos(
                        enemyNode,
                        enemy.SizeRect,
                        node,
                        unit.SizeRect
                    );

                    // Invisible이 아니면 LOS가 있음
                    if (los.CoverType != LosCalculations.CoverType.Invisible) hasAnyLos = true;

                    // 엄폐물 점수
                    switch (los.CoverType)
                    {
                        case LosCalculations.CoverType.Invisible:
                            totalCoverScore += 40f;
                            break;
                        case LosCalculations.CoverType.Full:
                            totalCoverScore += 30f;
                            break;
                        case LosCalculations.CoverType.Half:
                            totalCoverScore += 15f;
                            break;
                    }

                    if (los.CoverType > score.BestCover)
                        score.BestCover = los.CoverType;
                }
                catch { }
            }

            score.CoverScore = totalCoverScore / Math.Max(1, enemies.Count);
            score.HasLosToEnemy = hasAnyLos;

            // 목표에 따른 거리 점수 계산
            switch (goal)
            {
                case MovementGoal.FindCover:
                case MovementGoal.Retreat:
                    // 멀수록 좋음
                    score.DistanceScore = Math.Min(30f, nearestEnemyDist * 2f);
                    break;

                case MovementGoal.MaintainDistance:
                    // 목표 거리에 가까울수록 좋음
                    float distDiff = Math.Abs(nearestEnemyDist - targetDistance);
                    score.DistanceScore = Math.Max(0f, 20f - distDiff * 2f);
                    break;

                case MovementGoal.ApproachEnemy:
                    // 가까울수록 좋음
                    score.DistanceScore = Math.Max(0f, 30f - nearestEnemyDist * 2f);
                    break;

                case MovementGoal.AttackPosition:
                    // 사거리 내이면서 너무 가깝지 않음
                    if (nearestEnemyDist <= targetDistance && nearestEnemyDist >= 3f)
                        score.DistanceScore = 25f;
                    else if (nearestEnemyDist <= targetDistance)
                        score.DistanceScore = 15f;
                    else
                        score.DistanceScore = 0f;
                    break;

                case MovementGoal.RangedAttackPosition:
                    // ★ v2.2.43: 원거리 공격 위치 - 안전거리 유지 + 사거리 내
                    // targetDistance를 두 값으로 사용: 상위 16비트=사거리, 하위 16비트=최소거리
                    float weaponRange = targetDistance;
                    float minSafeDistance = 5f;  // 기본 안전거리

                    if (nearestEnemyDist < minSafeDistance)
                    {
                        // 너무 가까움 - 큰 페널티!
                        score.DistanceScore = -50f + nearestEnemyDist * 5f;
                    }
                    else if (nearestEnemyDist <= weaponRange)
                    {
                        // 안전거리 이상 + 사거리 내 = 최적!
                        // 멀수록 더 좋음 (최대 30점)
                        float distRatio = (nearestEnemyDist - minSafeDistance) / (weaponRange - minSafeDistance);
                        score.DistanceScore = 20f + distRatio * 10f;
                    }
                    else
                    {
                        // 사거리 밖 - 가까워져야 함
                        score.DistanceScore = Math.Max(0f, 10f - (nearestEnemyDist - weaponRange) * 2f);
                    }
                    break;
            }

            // 위협 점수 (기회 공격, AoE 등)
            score.ThreatScore = cell.ProvokedAttacks * 20f + cell.EnteredAoE * 15f;

            // 공격 가능성 점수 (LOS가 있고 사거리 내)
            if (hasAnyLos && nearestEnemyDist <= targetDistance)
                score.AttackScore = 20f;

            return score;
        }

        #endregion

        #region Best Position Finding

        /// <summary>
        /// 최적의 엄폐 위치 찾기
        /// </summary>
        public static async Task<PositionScore> FindBestCoverPosition(
            BaseUnitEntity unit,
            List<BaseUnitEntity> enemies,
            float minDistance = 5f)
        {
            var tiles = await FindAllReachableTiles(unit);
            if (tiles.Count == 0) return null;

            var scores = EvaluateAllPositions(unit, tiles, enemies, MovementGoal.FindCover, minDistance);
            var best = scores.FirstOrDefault(s => s.CanStand);

            if (best != null)
            {
                Main.Log($"[MovementAPI] {unit.CharacterName}: Best cover at ({best.Position.x:F1},{best.Position.z:F1}) - {best}");
            }

            return best;
        }

        /// <summary>
        /// 공격 가능 위치 찾기
        /// </summary>
        public static async Task<PositionScore> FindAttackPosition(
            BaseUnitEntity unit,
            List<BaseUnitEntity> enemies,
            float weaponRange = 15f)
        {
            var tiles = await FindAllReachableTiles(unit);
            if (tiles.Count == 0) return null;

            var scores = EvaluateAllPositions(unit, tiles, enemies, MovementGoal.AttackPosition, weaponRange);

            // LOS가 있고 공격 가능한 위치 우선
            var best = scores.FirstOrDefault(s => s.CanStand && s.HasLosToEnemy && s.AttackScore > 0);

            if (best == null)
            {
                // LOS 있는 위치라도
                best = scores.FirstOrDefault(s => s.CanStand && s.HasLosToEnemy);
            }

            if (best != null)
            {
                Main.Log($"[MovementAPI] {unit.CharacterName}: Attack position at ({best.Position.x:F1},{best.Position.z:F1}) - {best}");
            }

            return best;
        }

        /// <summary>
        /// 후퇴 위치 찾기
        /// </summary>
        public static async Task<PositionScore> FindRetreatPosition(
            BaseUnitEntity unit,
            List<BaseUnitEntity> enemies)
        {
            var tiles = await FindAllReachableTiles(unit);
            if (tiles.Count == 0) return null;

            var scores = EvaluateAllPositions(unit, tiles, enemies, MovementGoal.Retreat);
            var best = scores.FirstOrDefault(s => s.CanStand);

            if (best != null)
            {
                Main.Log($"[MovementAPI] {unit.CharacterName}: Retreat position at ({best.Position.x:F1},{best.Position.z:F1}) - {best}");
            }

            return best;
        }

        /// <summary>
        /// ★ v2.2.43: 원거리 공격 최적 위치 찾기
        /// - 안전거리(minSafeDistance) 이상 유지
        /// - 사거리(weaponRange) 내
        /// - LOS 확보
        /// - 엄폐물 선호
        /// </summary>
        public static async Task<PositionScore> FindRangedAttackPosition(
            BaseUnitEntity unit,
            List<BaseUnitEntity> enemies,
            float weaponRange = 15f,
            float minSafeDistance = 5f)
        {
            var tiles = await FindAllReachableTiles(unit);
            if (tiles.Count == 0)
            {
                Main.LogDebug($"[MovementAPI] {unit.CharacterName}: No reachable tiles for ranged attack");
                return null;
            }

            var scores = EvaluateAllPositions(unit, tiles, enemies, MovementGoal.RangedAttackPosition, weaponRange);

            // 1순위: 안전거리 + 사거리 내 + LOS + 공격 가능
            var best = scores.FirstOrDefault(s =>
                s.CanStand &&
                s.HasLosToEnemy &&
                s.DistanceScore >= 20f &&  // 안전거리 이상
                s.AttackScore > 0);

            // 2순위: 안전거리 + LOS (사거리 약간 벗어나도 됨)
            if (best == null)
            {
                best = scores.FirstOrDefault(s =>
                    s.CanStand &&
                    s.HasLosToEnemy &&
                    s.DistanceScore > 0f);  // 안전거리 이상
            }

            // 3순위: LOS만 확보 (안전거리 못 맞춰도)
            if (best == null)
            {
                best = scores.FirstOrDefault(s =>
                    s.CanStand &&
                    s.HasLosToEnemy);
            }

            if (best != null)
            {
                Main.Log($"[MovementAPI] ★ {unit.CharacterName}: Ranged attack position at ({best.Position.x:F1},{best.Position.z:F1}) - {best}");
            }
            else
            {
                Main.LogDebug($"[MovementAPI] {unit.CharacterName}: No suitable ranged attack position found");
            }

            return best;
        }

        /// <summary>
        /// ★ v2.2.43: 동기 버전 - 원거리 공격 최적 위치 찾기
        /// </summary>
        public static PositionScore FindRangedAttackPositionSync(
            BaseUnitEntity unit,
            List<BaseUnitEntity> enemies,
            float weaponRange = 15f,
            float minSafeDistance = 5f)
        {
            var tiles = FindAllReachableTilesSync(unit);
            if (tiles == null || tiles.Count == 0)
            {
                Main.LogDebug($"[MovementAPI] {unit.CharacterName}: No reachable tiles (sync)");
                return null;
            }

            // WarhammerPathPlayerCell → WarhammerPathAiCell 변환
            // ★ v2.2.43: readonly struct는 생성자로 초기화
            var aiCells = new Dictionary<GraphNode, WarhammerPathAiCell>();
            foreach (var kvp in tiles)
            {
                var playerCell = kvp.Value;
                var node = playerCell.Node as CustomGridNodeBase;
                if (node == null) continue;

                // WarhammerPathAiCell(position, diagonalsCount, length, node, parentNode, isCanStand, enteredAoE, stepsInsideDamagingAoE, provokedAttacks)
                var aiCell = new WarhammerPathAiCell(
                    node.Vector3Position,   // position
                    0,                       // diagonalsCount
                    playerCell.Length,       // length
                    node,                    // node
                    null,                    // parentNode
                    playerCell.IsCanStand,   // isCanStand
                    0,                       // enteredAoE
                    0,                       // stepsInsideDamagingAoE
                    0                        // provokedAttacks
                );
                aiCells[kvp.Key] = aiCell;
            }

            var scores = EvaluateAllPositions(unit, aiCells, enemies, MovementGoal.RangedAttackPosition, weaponRange);

            // 1순위: 안전거리 + 사거리 내 + LOS
            var best = scores.FirstOrDefault(s =>
                s.CanStand &&
                s.HasLosToEnemy &&
                s.DistanceScore >= 20f);

            // 2순위: LOS + 안전거리
            if (best == null)
            {
                best = scores.FirstOrDefault(s =>
                    s.CanStand &&
                    s.HasLosToEnemy &&
                    s.DistanceScore > 0f);
            }

            // 3순위: LOS만
            if (best == null)
            {
                best = scores.FirstOrDefault(s =>
                    s.CanStand &&
                    s.HasLosToEnemy);
            }

            if (best != null)
            {
                Main.Log($"[MovementAPI] ★ {unit.CharacterName}: Ranged attack position (sync) at ({best.Position.x:F1},{best.Position.z:F1}) - {best}");
            }

            return best;
        }

        #endregion

        #region Path Creation

        /// <summary>
        /// 특정 노드까지의 경로 생성
        /// </summary>
        public static ForcedPath CreatePathTo(
            CustomGridNodeBase targetNode,
            Dictionary<GraphNode, WarhammerPathAiCell> reachableTiles)
        {
            if (targetNode == null || reachableTiles == null || !reachableTiles.ContainsKey(targetNode))
                return null;

            try
            {
                return WarhammerPathHelper.ConstructPathTo(targetNode, reachableTiles);
            }
            catch (Exception ex)
            {
                Main.LogDebug($"[MovementAPI] CreatePathTo error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 동기 버전 경로 생성
        /// </summary>
        public static ForcedPath CreatePathToSync(
            Vector3 targetPosition,
            Dictionary<GraphNode, WarhammerPathPlayerCell> reachableTiles)
        {
            if (reachableTiles == null || reachableTiles.Count == 0)
                return null;

            try
            {
                return WarhammerPathHelper.ConstructPathTo(targetPosition, reachableTiles);
            }
            catch (Exception ex)
            {
                Main.LogDebug($"[MovementAPI] CreatePathToSync error: {ex.Message}");
                return null;
            }
        }

        #endregion

        #region Movement Execution

        /// <summary>
        /// 유닛을 특정 위치로 이동
        /// </summary>
        public static async Task<bool> MoveToPosition(
            BaseUnitEntity unit,
            PositionScore targetPosition)
        {
            if (unit == null || targetPosition == null || targetPosition.Node == null)
                return false;

            try
            {
                // 현재 위치와 같으면 이동 불필요
                var currentNode = unit.Position.GetNearestNodeXZ();
                if (currentNode == targetPosition.Node)
                {
                    Main.LogDebug($"[MovementAPI] {unit.CharacterName}: Already at target position");
                    return false;
                }

                // 도달 가능 타일 다시 계산 (경로 생성용)
                var tiles = await FindAllReachableTiles(unit);
                if (!tiles.ContainsKey(targetPosition.Node))
                {
                    Main.LogDebug($"[MovementAPI] {unit.CharacterName}: Target not reachable");
                    return false;
                }

                // 경로 생성
                var path = CreatePathTo(targetPosition.Node, tiles);
                if (path == null || path.path.Count < 2)
                {
                    Main.LogDebug($"[MovementAPI] {unit.CharacterName}: Failed to create path");
                    return false;
                }

                path.Claim(unit);

                // 이동 비용 계산
                var costRule = Rulebook.Trigger(new RuleCalculateMovementCost(unit, path));

                // 이동 명령 생성
                float apPerCell = unit.Blueprint.WarhammerMovementApPerCell;
                var moveParams = new UnitMoveToProperParams(path, apPerCell, costRule.ResultAPCostPerPoint);

                // 명령 실행
                unit.Commands.Run(moveParams);

                path.Release(unit);

                Main.Log($"[MovementAPI] {unit.CharacterName}: Moving to ({targetPosition.Position.x:F1},{targetPosition.Position.z:F1}) AP cost={targetPosition.APCost:F1}");
                return true;
            }
            catch (Exception ex)
            {
                Main.LogDebug($"[MovementAPI] MoveToPosition error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 간편 이동 - 최적 엄폐 위치로
        /// </summary>
        public static async Task<bool> MoveToCover(BaseUnitEntity unit, List<BaseUnitEntity> enemies)
        {
            var position = await FindBestCoverPosition(unit, enemies);
            if (position == null) return false;
            return await MoveToPosition(unit, position);
        }

        /// <summary>
        /// 간편 이동 - 공격 위치로
        /// </summary>
        public static async Task<bool> MoveToAttackPosition(
            BaseUnitEntity unit,
            List<BaseUnitEntity> enemies,
            float weaponRange = 15f)
        {
            var position = await FindAttackPosition(unit, enemies, weaponRange);
            if (position == null) return false;
            return await MoveToPosition(unit, position);
        }

        /// <summary>
        /// 간편 이동 - 후퇴
        /// </summary>
        public static async Task<bool> Retreat(BaseUnitEntity unit, List<BaseUnitEntity> enemies)
        {
            var position = await FindRetreatPosition(unit, enemies);
            if (position == null) return false;
            return await MoveToPosition(unit, position);
        }

        #endregion

        #region Utility Methods

        /// <summary>
        /// 특정 위치가 엄폐물 뒤인지 확인
        /// </summary>
        public static LosCalculations.CoverType GetCoverFromEnemy(
            BaseUnitEntity unit,
            Vector3 position,
            BaseUnitEntity enemy)
        {
            if (unit == null || enemy == null) return LosCalculations.CoverType.None;

            try
            {
                var posNode = position.GetNearestNodeXZ();
                var enemyNode = enemy.Position.GetNearestNodeXZ();

                if (posNode == null || enemyNode == null)
                    return LosCalculations.CoverType.None;

                var los = LosCalculations.GetWarhammerLos(
                    enemyNode,
                    enemy.SizeRect,
                    posNode,
                    unit.SizeRect
                );

                return los.CoverType;
            }
            catch
            {
                return LosCalculations.CoverType.None;
            }
        }

        /// <summary>
        /// 위치에서 적에게 LOS가 있는지 확인
        /// </summary>
        public static bool HasLosFromPosition(Vector3 position, BaseUnitEntity target, IntRect shooterSize)
        {
            if (target == null) return false;

            try
            {
                var posNode = position.GetNearestNodeXZ();
                var targetNode = target.Position.GetNearestNodeXZ();

                if (posNode == null || targetNode == null) return false;

                return LosCalculations.HasLos(posNode, shooterSize, targetNode, target.SizeRect);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 두 위치 간 이동 비용 계산 (셀 단위)
        /// </summary>
        public static int GetMovementCostInCells(Vector3 from, Vector3 to)
        {
            try
            {
                return WarhammerGeometryUtils.DistanceToInCells(from, default(IntRect), to, default(IntRect));
            }
            catch
            {
                return int.MaxValue;
            }
        }

        /// <summary>
        /// 유닛의 현재 노드 얻기
        /// </summary>
        public static CustomGridNodeBase GetUnitNode(BaseUnitEntity unit)
        {
            return unit?.Position.GetNearestNodeXZ();
        }

        #endregion
    }
}
