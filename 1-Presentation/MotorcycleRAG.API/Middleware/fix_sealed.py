import os
import sys

# Files to fix
middleware_files = [
    'CorrelationIdMiddleware.cs',
    'SecurityHeadersMiddleware.cs',
    'ExceptionHandlingMiddleware.cs',
    'HostHeaderValidationMiddleware.cs',
    'AuthorizationMiddleware.cs'
]

# Fix each file
for filename in middleware_files:
    filepath = os.path.join(os.getcwd(), filename)
    with open(filepath, 'r') as f:
        content = f.read()
        new_content = content.replace('internal sealed', 'public sealed', 1)
        with open(filepath, 'w') as fw:
            fw.write(new_content)
        print(f'Fixed: {filename}')
