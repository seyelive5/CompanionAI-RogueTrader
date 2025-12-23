# CompanionAI v2.2 - Enhanced Companion AI for Rogue Trader

Warhammer 40,000: Rogue Trader용 컴패니언 AI 모드입니다. 파티원들이 전투에서 자동으로 최적의 행동을 수행합니다.

## Features

### Strategy-Based AI System
각 캐릭터의 역할에 맞는 전략을 선택할 수 있습니다:

- **Tank**: 적에게 돌진, 도발, 방어 스킬 우선 사용
- **DPS**: 공격력 극대화, 갭 클로저(Death from Above 등) 활용
- **Support**: 아군 버프/힐 우선, 안전한 위치 유지
- **Balanced**: 상황에 따라 유동적으로 대응

### Timing-Aware Ability System
스킬 타이밍을 자동으로 인식합니다:

- **PreCombatBuff**: 전투 시작 시 버프 (Blessing of the Omnissiah 등)
- **Normal**: 일반 공격/스킬
- **Finisher**: 마무리 스킬 (Execute 등)
- **TurnEnding**: 턴 종료 시 사용 (반응형 방어 스킬)
- **ActionExtender**: 추가 행동 부여 스킬 (Run and Gun 등)

### Smart Combat Features
- 갭 클로저 자동 감지 (Death from Above, Charge 등)
- LoS(시야) 문제 시 자동 우회 공격
- 소모품 충전 횟수 체크 (빈 아이템 사용 방지)
- HP 소모 스킬 안전 사용 (체력 낮을 때 자제)
- 전투 간 스킬 상태 자동 리셋

## Installation

### Requirements
- [Unity Mod Manager (UMM)](https://www.nexusmods.com/site/mods/21)
- Warhammer 40,000: Rogue Trader

### Install Steps
1. Unity Mod Manager 설치 및 Rogue Trader에 적용
2. 이 저장소를 다운로드하거나 Release에서 DLL 다운로드
3. DLL을 다음 경로에 복사:
   ```
   %userprofile%\AppData\LocalLow\Owlcat Games\Warhammer 40000 Rogue Trader\UnityModManager\CompanionAI_v2.2\
   ```
4. `Info.json` 파일도 같은 폴더에 복사
5. 게임 실행

## Usage

1. 게임 내에서 `Ctrl + F10`으로 UMM 메뉴 열기
2. CompanionAI v2.2 탭 선택
3. 각 캐릭터별로:
   - **Enable Custom AI** 체크
   - **Strategy** 선택 (Tank/DPS/Support/Balanced)
4. 전투 시작하면 자동으로 AI가 동작

## Building from Source

### Requirements
- Visual Studio 2022 또는 MSBuild
- .NET Framework 4.8.1

### Build
```bash
msbuild CompanionAI_v2.2.csproj /p:Configuration=Release
```

출력 파일: `bin/Release/net481/CompanionAI_v2.2.dll`

## Project Structure

```
CompanionAI_v2.2/
├── Core/
│   ├── AIOrchestrator.cs      # AI 실행 오케스트레이터
│   ├── AbilityRules.cs        # 스킬 타이밍 규칙 DB
│   ├── CombatHelpers.cs       # 전투 유틸리티
│   ├── CombatStateListener.cs # 전투 시작/종료 감지
│   ├── GameAPI.cs             # 게임 API 래퍼
│   └── IUnitStrategy.cs       # 전략 인터페이스
├── Patches/
│   ├── BrainReplacementPatch.cs  # TurnController 패치
│   └── CustomAIPatch.cs          # AI Brain 패치
├── Strategies/
│   ├── TimingAwareStrategy.cs # 기본 전략 (부모 클래스)
│   ├── TankStrategy.cs        # 탱커 전략
│   ├── DPSStrategy.cs         # 딜러 전략
│   ├── SupportStrategy.cs     # 서포터 전략
│   └── BalancedStrategy.cs    # 밸런스 전략
├── Settings/
│   └── ModSettings.cs         # 설정 관리
├── UI/
│   └── MainUI.cs              # UMM UI
├── Main.cs                    # 모드 진입점
└── Info.json                  # UMM 모드 정보
```

## Changelog

### v2.2.4
- 전투 상태 리스너 추가 (전투 간 스킬 상태 리셋)

### v2.2.3
- 반응형 방어 스킬 타이밍 수정 (TurnEnding)
- Death from Above 갭 클로저 지원
- Abelard 이동 안함 문제 수정 (LoS 폴백)

### v2.2.2
- Blood Ampoule 무한루프 수정 (charges=0 체크)
- HP 소모 스킬에서 소모품 제외

### v2.2.0
- 타이밍 기반 스킬 시스템 도입
- 전략별 AI 분리 (Tank/DPS/Support/Balanced)

## License

MIT License

## Credits

- Owlcat Games - Warhammer 40,000: Rogue Trader
- Unity Mod Manager Team
- Claude AI - Code assistance
