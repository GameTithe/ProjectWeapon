# KAN-78 일반 랭킹 귀환 기록 비교

일반 랭킹은 확정된 성공 귀환을 구역 번호, 해당 런의 보스 처치, 잔여 정수 밀리초, 신규 반출 가치 순서로 내림차순 비교한다. `GeneralRanking.CompareRank`가 0이면 공동 순위다. 목록의 `CompareDisplayOrder`는 공동 순위를 이른 달성 UTC와 제출 ID 서수 순서로 정렬한다. [KAN-78](https://gametithe.atlassian.net/browse/KAN-78) · [KAN-61](https://gametithe.atlassian.net/browse/KAN-61)

호출자는 승인된 성공 정산의 `RunResult`, 같은 런의 `SortieManifest`, 이 정산으로 회수한 실물 목록과 승인된 보스·제출 정보를 사용한다. `WeaponExportValues.Calculate`는 반입품과 대여품을 제외하고 신규 실물 ID마다 한 번 값을 더한다. 같은 원형을 서로 다른 ID로 반출하면 각각 집계한다. 등급은 해당 런에 적용된 무기 카탈로그에서 확인한다. 등급별 가안은 `WeaponExportValues.Prototype`의 10·25·60·140·300이며 배치나 서버는 구성값을 주입할 수 있다.

`GeneralRankingRecord.TryCreate`는 사망이나 시간초과 시 `false`를 반환한다. 호출자는 이 경우 기존 최고 기록을 보존한다. 후보가 있으면 `CompareRank(candidate, best) < 0`일 때만 갱신한다. 저장된 기록은 승인 당시의 필드로 다시 구성할 수 있다. 기록 확정과 저장 연결은 KAN-118이 담당한다. [KAN-118](https://gametithe.atlassian.net/browse/KAN-118)
