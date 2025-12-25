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

### 버전 관리
- `Info.json` 버전 업데이트 필수
- 변경사항 주석에 버전 명시 (예: `// ★ v2.2.28:`)
