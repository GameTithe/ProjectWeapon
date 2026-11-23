# KAN-99 단일 코인 뽑기 거래

`CoinWeaponDrawService`에 로컬 저장소, KAN-69의 검증된 추첨표와 단일 가격을 전달한다. 기본 가격 `PrototypeSingleDrawCost`는 **500코인 가안**이다. 호출자는 결제 요청마다 고유한 `Guid`를 만들고 재시도에 같은 ID를 쓴다. [KAN-99](https://gametithe.atlassian.net/browse/KAN-99) · [KAN-69](https://gametithe.atlassian.net/browse/KAN-69) · [KAN-73](KAN-73-atomic-snapshot.md)

## 저장과 재시도

- `Purchase(requestId, nextExclusive)`는 확정 잔액을 확인하고 추첨으로 무기 한 개를 개인 소유 창고 무기로 발급하는 후보를 만든다. KAN-73 저장소에서 코인 차감, 무기와 영수증을 같은 리비전으로 커밋한다. 도감과 런 정산 기록은 유지한다.
- 정상 구매는 `Purchased`와 기록된 영수증을 반환한다. 이미 같은 가격으로 확정된 ID는 `AlreadyPurchased`로 원래 원형·실물 ID·등급·차감액·당시 잔고를 읽는다. 그 실물을 나중에 잃어도 중복 요청이 다시 발급하지 않는다. 같은 ID에 가격이 다르면 `RequestConflict`다.
- 잔액 부족은 `InsufficientFunds`다. 다른 거래가 먼저 저장됐고 이 요청이 여전히 미확정이면 `RevisionConflict`다. 실패한 I/O의 응답이 유실됐다면 같은 ID로 재시도한다. 이전 커밋이 완료돼 있다면 무기 추첨 함수를 실행하지 않는다.
- 기존 포맷 1에서 뽑기 영수증 필드가 빠진 파일은 빈 영수증 목록으로 읽는다. 런 정산과 뽑기 영수증의 ID는 별도 영역이다. 뽑기 외의 거래가 새 스냅샷을 조립할 때 기존 `CoinWeaponDraws`도 함께 전달해 중복 방지 기록을 보존한다.

## 테스트 재현

```powershell
unity test . --mode EditMode --filter ProjectWeapon.CoinWeaponDraw.Tests --output "$env:TEMP/KAN-99-results.xml"
```

검증에는 KAN-69 계약을 충족하는 테스트용 등급별 풀을 쓴다. 잔액 499/500, 도감 보존, 중복·가격 충돌, 두 저장 후보의 리비전 경합, 교체 전 실패와 교체 후 응답 유실을 검증한다. Windows 파일 잠금으로 실제 교체가 실패하는 경우도 재현한다. 게임의 전체 등급별 콘텐츠 구성과 UI는 별도 작업이다.
