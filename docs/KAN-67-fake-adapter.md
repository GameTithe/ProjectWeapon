# KAN-67 가짜 보상형 광고 어댑터

`IRewardedAdAdapter`로 요청을 시작하고 `OutcomeReported`에서 결과를 받는다. `FakeRewardedAdAdapter`는 네트워크 없이 호출자가 넣은 결과를 재현한다. [KAN-67](https://gametithe.atlassian.net/browse/KAN-67) · [KAN-19 공통 계약](KAN-19-domain-contracts.md)

- `TryBegin(requestId)`에 호출자가 만든 비어 있지 않은 요청 GUID를 넣는다. 같은 가짜 어댑터에서 이미 시작했거나 종료한 요청 ID는 다시 등록할 수 없다.
- `TryInjectCallback`에 요청 ID와 종료 상태를 넣는다. 로드 실패, 취소, 확인 없이 닫힘, 오류는 해당 상태를 알리고 `VerifiedCompletion`은 비어 있다.
- 확인된 완료는 시작한 요청 ID와 비어 있지 않은 테스트 완료 식별자가 모두 있어야 한다. 수락된 경우에만 `AdCompletionVerified`를 결과에 포함한다. 테스트 증빙은 호출자가 주입하며, 인증 형식과 제공자 검증 방식은 KAN-55의 결정을 따른다. [KAN-55](https://gametithe.atlassian.net/browse/KAN-55)
- 등록하지 않은 요청, 이미 종료한 요청, 재사용한 완료 식별자와 형식 오류의 콜백은 `false`를 반환하며 상태 알림도 내보내지 않는다. 확인 없는 종료를 받은 요청에 뒤늦게 완료 콜백이 도착해도 같은 요청은 다시 열리지 않는다.
- 어댑터는 재화, 무기, 사용 횟수를 기록하지 않는다. 나중에 조정 계층이 검증 완료 식별자로 지급 요청과 사용 횟수 반영을 구성한다. 테스트 중복 방지는 어댑터 인스턴스 수명 내에서만 적용한다. [KAN-97](https://gametithe.atlassian.net/browse/KAN-97)

## 재현 절차

Unity 프로젝트 루트의 PowerShell에서 실행한다.

```powershell
unity test . --mode EditMode --filter ProjectWeapon.Ad.Tests --output "$env:TEMP/KAN-67-editmode.xml" --timeout 900
```

| 주입 사례 | 기대 결과 |
| --- | --- |
| 로드 실패·취소·확인 없이 닫힘·오류 | 종료 알림 한 번, 검증 완료 값 없음 |
| 요청 ID 없음 또는 등록한 요청과 다름 | 알림 없음, 정상 요청은 이후 처리 가능 |
| 확인 완료에 증빙이 없음 | 알림 없음, 확인 없이 닫힘을 이어서 주입하면 검증 완료 값 없는 종료 |
| 정상 완료 후 동일 요청 재전달 또는 다른 요청에 동일 증빙 재사용 | 새 알림 없음, 다른 요청은 자기 증빙으로 완료 가능 |
| 같은 요청의 완료 콜백 두 개를 동시에 제출 | 검증 완료 값이 있는 알림 한 번 |


## 이번 작업의 검증 결과

2026-11-23 Unity 6000.6.9f1 및 Unity CLI 1.0.1에서 광고 EditMode 테스트 10건을 실행했고 모두 통과했다. XML과 실행 로그의 로컬 위치는 `%TEMP%/ProjectWeapon-KAN67-1795432082`다.
