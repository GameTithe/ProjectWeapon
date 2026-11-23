# KAN-191 일반 무기 원형 5종

기획서 v3.0 4쪽 §6의 일반 무기와 8쪽 §12의 원형 데이터 원칙을 반영한다. 첫 통합에 쓰는 무기는 낡은 검이다. [KAN-191](https://gametithe.atlassian.net/browse/KAN-191) · [KAN-187](https://gametithe.atlassian.net/browse/KAN-187) · [KAN-193](https://gametithe.atlassian.net/browse/KAN-193)

| 고정 원형 ID | 표시 이름 | 종류 | 검증용 기본 공격 패턴 | 검증용 스킬 패턴 |
| --- | --- | --- | --- | --- |
| `old_sword` | 낡은 검 | 검 | 근접 베기 | 전방 넓게 베기 |
| `wooden_spear` | 나무 창 | 창 | 직선 찌르기 | 연속 찌르기 |
| `hunting_bow` | 사냥 활 | 활 | 단일 화살 | 화살 3발 |
| `mace` | 철퇴 | 철퇴 | 근접 원형 타격 | 주변 충격파 |
| `dagger` | 단검 | 단검 | 빠른 연속 타격 | 순간 연속 베기 |

모두 1등급이다. 패턴 이름은 후속 행동 작업을 위한 기획 데이터다. 실제 기본 공격·스킬·VFX 참조와 피해·쿨다운은 해당 구현에서 채운다. KAN-129에서 `old_sword`의 기본 공격 참조를 연결하고 그 항목을 검증한다. [KAN-129](https://gametithe.atlassian.net/browse/KAN-129)

## 조회와 검증

`Assets/ProjectWeapon/Weapons/PrototypeStarter/PrototypeStarterWeaponCatalog.asset`을 런타임 조립 지점에 직렬화된 에셋 참조로 지정한다. 시작할 때 `WeaponCatalog.CreateLookup()`으로 조회용 읽기 전용 사전을 만든다. 저장한 문자열이나 획득 데이터에서 읽은 문자열을 `WeaponDefinitionId`로 생성해 같은 사전에서 조회한다. 문자열 비교에는 대소문자가 적용된다. 미등록 ID는 사전의 `TryGetValue()`에서 실패 결과를 반환한다.

`CreateLookup()`은 `WeaponDefinitionValidator.ValidateCatalogMetadata()`로 참조된 원형, ID, 이름, 등급과 종류를 검사한다. ID 중복이나 잘못된 행이 있으면 예외가 발생한다. 기존 `Validate()` 및 에디터 메뉴의 전체 검사는 기본 공격·스킬·VFX 참조까지 검사한다.

## 테스트 재현

```powershell
unity test . --mode EditMode --filter ProjectWeapon.Weapons.Tests --output "$env:TEMP/KAN-191-results.xml"
```

직렬화된 카탈로그에서 다섯 원형의 ID와 설정을 확인한다. 저장 참조의 JSON 왕복과 획득 ID 조회가 같은 에셋을 가리키는지 검사하고, 미등록·중복·누락과 메타데이터 검사 및 전체 검사의 차이를 테스트한다.
