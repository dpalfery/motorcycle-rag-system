FROM python:3.9-slim

# Install system dependencies
RUN apt-get update && apt-get install -y \
    gcc \
    g++ \
    git \
    curl \
    wget \
    && rm -rf /var/lib/apt/lists/*

# Install NVIDIA Container Toolkit
RUN mkdir -p /etc/nvidia-container-runtime
RUN echo '{"compute_library_version": "20201111"}' > /etc/nvidia-container-runtime/config.toml

# Install NVIDIA driver
RUN apt-get update && apt-get install -y --no-install-recommends \
    nvidia-driver-470 \
    && rm -rf /var/lib/apt/lists/*

# Install NVIDIA Container Toolkit
RUN curl -fsSL https://nvidia.github.io/nvidia-docker/gpgkey | apt-key add - \
    && distribution=$(. /etc/os-release;echo $ID$VERSION_ID) \
    && curl -fsSL https://nvidia.github.io/nvidia-docker/$distribution/nvidia-docker.list | tee /etc/apt/sources.list.d/nvidia-docker.list

# Install Python dependencies
WORKDIR /app
COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

# Copy application code
COPY . .

# Create non-root user
RUN useradd --create-home --shell /bin/bash app && chown -R app:app /app
USER app

# Expose port
EXPOSE 8100

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=5s --retries=3 \
    CMD curl -f http://localhost:8100/health || exit 1

# Run the application
CMD ["uvicorn", "main:app", "--host", "0.0.0.0", "--port", "8100", "--reload"]