import React from 'react';
import Alert from 'Components/Alert';
import Label from 'Components/Label';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import TableRow from 'Components/Table/TableRow';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import { kinds } from 'Helpers/Props';

export interface RemoteSyncHistoryItem {
  id: number;
  tvdbId: number;
  title: string;
  action: string;
  detail?: string | null;
  syncedAt: string;
}

function RemoteSyncTable() {
  const {
    data: rows = [],
    isLoading,
    error,
  } = useApiQuery<RemoteSyncHistoryItem[]>({
    path: '/remotesync',
  });

  const columns = [
    { name: 'syncedAt', label: 'When', isVisible: true },
    { name: 'title', label: 'Title', isVisible: true },
    { name: 'action', label: 'Action', isVisible: true },
    { name: 'detail', label: 'Detail', isVisible: true },
  ];

  if (isLoading) {
    return <LoadingIndicator />;
  }

  if (error) {
    return (
      <Alert kind={kinds.DANGER}>Unable to load remote sync history.</Alert>
    );
  }

  const addedCount = rows.filter(
    (r) => r.action?.toLowerCase() === 'added'
  ).length;
  const failedCount = rows.filter(
    (r) => r.action?.toLowerCase() === 'failed'
  ).length;

  return (
    <div>
      <div
        style={{
          display: 'flex',
          gap: '8px',
          alignItems: 'center',
          marginBottom: '12px',
        }}
      >
        <Label kind={kinds.SUCCESS}>Added: {addedCount}</Label>
        <Label kind={failedCount > 0 ? kinds.DANGER : kinds.DEFAULT}>
          Failed: {failedCount}
        </Label>
      </div>

      {rows.length ? (
        <Table columns={columns}>
          <TableBody>
            {rows.map((row) => {
              const isFailed = row.action?.toLowerCase() === 'failed';

              return (
                <TableRow key={row.id}>
                  <TableRowCell>
                    {new Date(row.syncedAt).toLocaleString()}
                  </TableRowCell>
                  <TableRowCell>{row.title}</TableRowCell>
                  <TableRowCell>
                    <Label kind={isFailed ? kinds.DANGER : kinds.SUCCESS}>
                      {row.action}
                    </Label>
                  </TableRowCell>
                  <TableRowCell>{row.detail || '-'}</TableRowCell>
                </TableRow>
              );
            })}
          </TableBody>
        </Table>
      ) : (
        <Alert kind={kinds.INFO}>No remote sync history recorded yet.</Alert>
      )}
    </div>
  );
}

export default RemoteSyncTable;
