# CompanionAI v2.2 - Warhammer 40K Rogue Trader AI Mod

## 프로젝트 개요
- **언어**: C# (.NET Framework 4.8.1)
- **타입**: Unity Mod Manager 기반 게임 모드
- **목적**: 동료 AI 완전 대체 - 타이밍 인식 전략 시스템

## 폴더 구조
```
CompanionAI_v2.2/
├── Core/           - AI 핵심 로직 (AIOrchestrator, GameAPI, AbilityRules 등)
├── Patches/        - Harmony 패치 (CustomAIPatch, BrainReplacementPatch)
├── Strategies/     - 역할별 전략 (DPS, Tank, Support, Balanced)
├── Settings/       - 설정 및 로컬라이제이션
├── UI/             - Unity Mod Manager UI
└── bin/Release/    - 빌드 산출물
```

## 빌드 명령
```powershell
"C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" CompanionAI_v2.2.csproj /p:Configuration=Release /v:minimal /nologo
```

---

# Claude 행동 방침

## ⚠️ 최우선 규칙: 대화 시작/리셋 시 전체 코드 파악

**대화가 리셋되거나 새로 시작될 때, 수정 제안 전에 반드시:**
1. 핵심 파일들을 먼저 읽고 전체 아키텍처 파악
2. 특히 무한 루프 방지 메커니즘들의 상호작용 이해
3. 이전에 시도했다가 문제가 된 접근법 확인 (프레임 기반 등)
4. 기존 안전장치들이 왜 존재하는지 이유 파악

**핵심 파일 목록:**
- `CustomAIPatch.cs` - 무한 루프 방지 메커니즘들
- `AbilityUsageTracker.cs` - 중앙화된 스킬 추적
- `AIOrchestrator.cs` - 전체 AI 흐름
- `TimingAwareStrategy.cs` - 전략 베이스 클래스

## 핵심 원칙: "나무를 보지 말고 숲을 봐라"

### 적극적 문제 해결
- 질문의 근본 원인까지 파악
- 더 나은 솔루션 주도적 제안
- 복잡한 리팩토링도 거리낌 없이 진행
- 표면적 증상이 아닌 구조적 문제 해결

### 완전한 구현
- 분석 → 설계 → 구현 → 테스트 한 번에
- 여러 파일 동시 수정 OK
- 아키텍처 개선 적극 제안
- 관련된 모든 파일 함께 업데이트

### 금지 사항
- 쉬운 해결책은 객관적으로 정말로 이게 가장 최고의 선택이라고 판단될 때만 제시
- "나중에 하세요" 같은 미루기 금지
- 부분적 수정 대신 완전하고 전체적인 해결
- 임시방편/땜빵 코드 작성 금지

### 코드 품질
- 중복 코드 발견 시 즉시 리팩토링
- 성능 최적화 기회 발견 시 제안
- 버그 가능성 발견 시 선제적 수정
- 미사용 코드/변수 정리

### 분석 태도
- 전체 코드베이스 맥락에서 문제 파악
- 사이드이펙트 철저히 검토
- 게임 디컴파일 소스 적극 활용하여 정확한 동작 파악
- 추측보다 조사 우선

---

## 프로젝트 특화 지침

### 참조 리소스
- **게임 디컴파일 소스**: `C:\Users\veria\Downloads\EnhancedCompanionAI (2)\RogueTraderDecompiled-master`
- **스킬 데이터**: `C:\Users\veria\Downloads\CompanionAI_v2.2\Rogue_Trader_Skills.csv`
- **능력 덤프**: `C:\Users\veria\Downloads\CompanionAI_v2.2\CompanionAI_AllAbilities.txt`
- **게임 로그**: `C:\Users\veria\AppData\LocalLow\Owlcat Games\Warhammer 40000 Rogue Trader\GameLogFull.txt`

### 게임 API 연동
- GUID 기반 능력 식별 우선 (다국어 호환)
- 키워드 기반은 폴백으로만 사용

### 전략 시스템
- 모든 전략은 `TimingAwareStrategy` 상속
- Phase 기반 우선순위 결정
- `AbilityUsageTracker`로 중앙화된 추적

### ⚠️ RangePreference 규칙 (v2.2.40+)
**Single Source of Truth: `CombatHelpers.cs`의 헬퍼 함수 사용 필수**

```csharp
// ✅ 올바른 방법
bool preferRanged = CombatHelpers.ShouldPreferRanged(settings);
var filtered = CombatHelpers.FilterAbilitiesByRangePreference(abilities, pref);
bool isPreferred = CombatHelpers.IsPreferredWeaponType(ability, pref);

// ❌ 금지 - Role 기반 추론 (레거시 패턴)
bool preferRanged = settings.Role == AIRole.DPS;  // 절대 금지!
```

**적용 위치:**
- `AIOrchestrator.cs` - PrimaryWeaponAttack 선택
- `TimingAwareStrategy.cs` - 공격 능력 필터링
- `CustomAIPatch.cs` - 폴백 능력 선택
- `GameAPI.cs` - FindAnyAttackAbility() 호출 시

**원칙:**
- RangePreference는 Role과 독립적인 설정
- Support + PreferRanged, Tank + PreferRanged 모두 가능
- 새로운 능력 선택 코드 작성 시 반드시 헬퍼 함수 사용

### 버전 관리
- `Info.json` 버전 업데이트 필수
- 변경사항 주석에 버전 명시 (예: `// ★ v2.2.28:`)

---

## 사역마(Pet/Familiar) 시스템

### 개요
- **Overseer** 아키타입 캐릭터가 사역마를 보유
- 사역마는 독립적으로 행동하지 않고, **주인의 명령 스킬**로 제어
- 주인이 AP를 소모하여 사역마에게 이동/공격/버프 명령

### 핵심 API (게임 소스)
```csharp
// 마스터 확인
unit.IsMaster                    // true면 펫 보유
unit.Pet                         // 펫 유닛 참조
unit.GetOptional<UnitPartPetOwner>()?.PetType  // 펫 타입

// 펫 확인
unit.IsPet                       // true면 펫
unit.Master                      // 마스터 유닛 참조

// 능력 범위
ability.Blueprint.AoERadius      // 버프/효과 범위
```

### 펫 타입 (PetType enum)
| 타입 | 설명 | 전략 |
|------|------|------|
| `Mastiff` | 사이버-마스티프 | 공격적 - 적에게 직접 공격 명령 |
| `Eagle` | 사이버-이글 | 교란 - 적 밀집 지역에 실명/방해 |
| `Raven` | 사이버-레이븐 | 사이킥 지원 - 사역마 위치에서 스킬 발사 |
| `ServoskullSwarm` | 서보-스컬 | 버프/디버프 - 아군 밀집 지역으로 이동 → 범위 버프 |
| `Servitor` | 서비터 | (확인 필요) |

### 주인이 사용하는 명령 스킬 패턴
- `*_Support_Ability` 또는 `*_SupportAbility` 접미사
- 예: `ServoskullPet_PrioritySignal_SupportAbility`, `MastiffPet_Apprehend_AbilitySupport`

### 서보스컬 명령 스킬 GUID
| 스킬 이름 | GUID | 설명 |
|-----------|------|------|
| 재배치 | `5376c2d18af1499db985fbde6d5fe1ce` | 서보스컬 이동 명령 |
| 우선 신호 | `33aa1b047d084a9b8faf534767a3a534` | 공격 버프 |
| 메디카 신호 | `62eeb81743734fc5b8fac71b34b14683` | 회복 버프 |
| 도발 신호 | `a8c7d8404d104d4dad2d460ec2b470ee` | 적 도발 |
| 외삽 | `d68b6efac32b4db7afaf7de694eab819` | 범위 확장 |

### 서보스컬 AI 로직 (구현 예정)
```
1. 주인 턴 시작 → IsMaster && PetType == ServoskullSwarm 확인
2. 아군 밀집 지역 계산 (버프 범위 내 최대 아군 커버)
3. 서보스컬 현재 위치 vs 최적 위치 비교
   - 차이가 크면 → "재배치" 스킬로 이동
4. 버프 신호 선택 (우선 신호 > 메디카 신호 > 도발 신호)
5. 서보스컬에게 버프 사용
6. 남은 AP로 일반 전략 로직 수행
```

### 핵심 게임 컴포넌트
- `UnitPartPetOwner` - 마스터의 펫 정보 (`PetUnit`, `PetType`, `HasPet`)
- `AbilityCanTargetOnlyPetUnits` - 펫만 타겟 가능한 능력 표시
- `WarhammerOverrideAbilityCasterPositionByPet` - 주인 스킬이 **펫 위치에서** 발동

### 참조 파일 (게임 디컴파일)
- `Code/Kingmaker/UnitLogic/Parts/UnitPartPetOwner.cs`
- `Code/Kingmaker/EntitySystem/Entities/BaseUnitEntity.cs` (IsPet, Master, Pet)
- `Code/Kingmaker/Designers/Mechanics/Facts/WarhammerOverrideAbilityCasterPositionByPet.cs`
- `Kingmaker.Enums/Kingmaker/Enums/PetType.cs`
